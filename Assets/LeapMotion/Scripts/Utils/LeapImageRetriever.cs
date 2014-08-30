/******************************************************************************\
* Copyright (C) Leap Motion, Inc. 2011-2014.                                   *
* Leap Motion proprietary. Licensed under Apache 2.0                           *
* Available at http://www.apache.org/licenses/LICENSE-2.0.html                 *
\******************************************************************************/

using UnityEngine;
using System.Collections;
using Leap;

// To use the LeapImageRetriever you must be on version 2.1+
// and enable "Allow Images" in the Leap Motion settings.
public class LeapImageRetriever : MonoBehaviour {
	
	private const string NORMAL_SHADER = "LeapMotion/LeapDistorted";
	private const string UNDISTORT_SHADER = "LeapMotion/LeapUndistorted";
	private const int DEFAULT_TEXTURE_WIDTH = 640;
	private const int DEFAULT_TEXTURE_HEIGHT = 240;
	private const int DEFAULT_DISTORTION_WIDTH = 64;
	private const int DEFAULT_DISTORTION_HEIGHT = 64;
	private const int IMAGE_WARNING_WAIT = 10;
	
	public int imageIndex = 0;
	public Color imageColor = Color.white;
	public float gammaCorrection = 1.0f;
	public bool undistortImage = true;
	public bool blackIsTransparent = true;
	//THRESHOLD FOR BLOB DETECTION
	private byte brightnessThreshold = 35;
	private bool[] mask = new bool[DEFAULT_TEXTURE_WIDTH*DEFAULT_TEXTURE_HEIGHT];
	private int PixelLimit = 1000000;
	private int PixelsFound = 0;
	private Vector Place;
	private Controller leap_controller_;
	
	// Main texture.
	private Texture2D main_texture_;
	private Color32[] image_pixels_;
	private byte[] image_data_;
	private int image_misses_ = 0;
	
	
	// Distortion textures.
	private Texture2D distortionX_;
	private Texture2D distortionY_;
	private Color32[] dist_pixelsX_;
	private Color32[] dist_pixelsY_;
	private float[] distortion_data_;
	
	private void SetMainTextureDimensions(int width, int height) {
		int num_pixels = width * height;
		main_texture_ = new Texture2D(width, height, TextureFormat.Alpha8, false);
		main_texture_.wrapMode = TextureWrapMode.Clamp;
		image_pixels_ = new Color32[num_pixels];
	}
	
	private void SetDistortionDimensions(int width, int height) {
		int num_pixels = width * height;
		distortionX_ = new Texture2D(width, height, TextureFormat.RGBA32, false);
		distortionY_ = new Texture2D(width, height, TextureFormat.RGBA32, false);
		distortionX_.wrapMode = TextureWrapMode.Clamp;
		distortionY_.wrapMode = TextureWrapMode.Clamp;
		
		dist_pixelsX_ = new Color32[num_pixels];
		dist_pixelsY_ = new Color32[num_pixels];
	}
	
	void Start() {
		leap_controller_ = new Controller();
		leap_controller_.SetPolicyFlags(Controller.PolicyFlag.POLICY_IMAGES);
		
		SetMainTextureDimensions(DEFAULT_TEXTURE_WIDTH, DEFAULT_TEXTURE_HEIGHT);
		SetDistortionDimensions(DEFAULT_DISTORTION_WIDTH, DEFAULT_DISTORTION_HEIGHT);
		
	}
	
	void Update () {
		if (undistortImage)
			renderer.material = new Material(Shader.Find(UNDISTORT_SHADER));
		else
			renderer.material = new Material(Shader.Find(NORMAL_SHADER));
		
		Frame frame = leap_controller_.Frame();
		
		if (frame.Images.Count == 0) {
			image_misses_++;
			if (image_misses_ == IMAGE_WARNING_WAIT) {
				Debug.LogWarning("Can't find any images. " +
				                 "Make sure you enabled 'Allow Images' in the Leap Motion Settings, " +
				                 "you are on tracking version 2.1+ and " +
				                 "your Leap Motion device is plugged in.");
			}
			return;
		}
		
		// Check main texture dimensions.
		Image image = frame.Images[imageIndex];
		int image_width = image.Width;
		int image_height = image.Height;
		if (image_width == 0 || image_height == 0) {
			Debug.LogWarning("No data in the image texture.");
			return;
		}
		
		if (image_width != main_texture_.width || image_height != main_texture_.height)
			SetMainTextureDimensions(image_width, image_height);
		
		// Check distortion texture dimensions.
		// Divide by two 2 because there are floats per pixel.
		int distortion_width = image.DistortionWidth / 2;
		int distortion_height = image.DistortionHeight;
		if (distortion_width == 0 || distortion_height == 0) {
			Debug.LogWarning("No data in the distortion texture.");
			return;
		}
		
		if (distortion_width != distortionX_.width || distortion_height != distortionX_.height)
			SetDistortionDimensions(distortion_width, distortion_height);
		
		// Load image texture data.
		image_data_ = image.Data;
		distortion_data_ = image.Distortion;
		mask = new bool[image_width*image_height];
		for (int j = 0; j < (image_width*image_height)-1; j++){
			mask[j] = false;
		}
		if(frame.Hands.Count>0){
			FindHandCenter(frame,true);
			PixelsFound = 0;
			BlobFind ((((int)Place.y*image_width)+((int)Place.x)),image_data_,0,mask);
			if(frame.Hands.Count>1){
				FindHandCenter(frame,false);
				PixelsFound = 0;
				BlobFind ((((int)Place.y*image_width)+((int)Place.x)),image_data_,0,mask);
			}
		}
		LoadMainTexture(mask);
		if (undistortImage)
			EncodeDistortion();
		ApplyDataToTextures();
		
		renderer.material.mainTexture = main_texture_;
		renderer.material.SetColor("_Color", imageColor);
		renderer.material.SetFloat("_GammaCorrection", gammaCorrection);
		renderer.material.SetInt("_BlackIsTransparent", blackIsTransparent ? 1 : 0);
		
		if (undistortImage) {
			renderer.material.SetTexture("_DistortX", distortionX_);
			renderer.material.SetTexture("_DistortY", distortionY_);
			
			renderer.material.SetFloat("_RayOffsetX", image.RayOffsetX);
			renderer.material.SetFloat("_RayOffsetY", image.RayOffsetY);
			renderer.material.SetFloat("_RayScaleX", image.RayScaleX);
			renderer.material.SetFloat("_RayScaleY", image.RayScaleY);
		}
		
		transform.localPosition = new Vector3(0,0,frame.Hands.Rightmost.PalmPosition.y*0.00155f);
		transform.localScale = new Vector3((frame.Hands.Rightmost.PalmPosition.y*0.00155f)*8,(frame.Hands.Rightmost.PalmPosition.y*0.00155f)*8,0);
	}
	
	void BlobFind(int Start, byte[] image, int Stack, bool[] mask){
		if(PixelsFound<PixelLimit && !mask[Start]){
			//MARK THIS PIXEL AS DONE SO WE DON'T REDO IT
			mask[Start] = true;
			PixelsFound++;
			//ASSIMILATE THE SURROUNDING PIXELS
			if(Stack<50000){//PREVENTS STACK OVERFLOW
				if( (Mathf.Floor(Start%main_texture_.width) !=0) && (image[Start-1] > brightnessThreshold)&&(!mask[Start-1]) ){
					BlobFind(Start-1, image, Stack+1, mask);
				}
				if( (Mathf.Floor(Start%main_texture_.width)!=main_texture_.width-1) && (image[Start+1] > brightnessThreshold)&&(!mask[Start+1]) ){
					BlobFind(Start+1, image, Stack+1, mask);
				}
				if( (Mathf.Floor(Start/main_texture_.width) !=0) && (image[Start-main_texture_.width] > brightnessThreshold)&&(!mask[Start-main_texture_.width]) ){
					BlobFind(Start-main_texture_.width, image, Stack+1, mask);
				}
				if( (Mathf.Floor(Start/main_texture_.width)!=main_texture_.height-1) && (image[Start+main_texture_.width] > brightnessThreshold)&&(!mask[Start+main_texture_.width]) ){
					BlobFind(Start+main_texture_.width, image, Stack+1, mask);
				}
			}
		}
	}
	
	void LoadMainTexture(bool[] mask) {
		int num_pixels = main_texture_.width * main_texture_.height;
		for (int i = 0; i < num_pixels; ++i){
			if(mask[i]){
				image_pixels_[i].a = image_data_[i];
			}else{
				image_pixels_[i].a = 0;
			}
		}
	}
	void FindHandCenter(Frame frame, bool r) {
		if(frame.Hands.Count>0){
			if(r){
				Place = frame.Images[imageIndex].Warp(new Vector(frame.Hands.Rightmost.PalmPosition.x/frame.Hands.Rightmost.PalmPosition.y*-1,frame.Hands.Rightmost.PalmPosition.z/frame.Hands.Rightmost.PalmPosition.y,0));
			}else{
				Place = frame.Images[imageIndex].Warp(new Vector(frame.Hands.Leftmost.PalmPosition.x/frame.Hands.Leftmost.PalmPosition.y*-1,frame.Hands.Leftmost.PalmPosition.z/frame.Hands.Leftmost.PalmPosition.y,0));
			}
			//return new Vector(pos.x,(pos.y*-1)+DEFAULT_TEXTURE_HEIGHT,0);
		}
	}
	
	// Encodes the float distortion texture as RGBA values to transfer the data to the shader.
	void EncodeDistortion() {
		// Move distortion data to distortion x and y textures.
		int num_distortion_floats = 2 * distortionX_.width * distortionX_.height;
		for (int i = 0; i < num_distortion_floats; ++i) {
			
			float dval = distortion_data_[i];
			// The distortion range is -0.6 to +1.7. Normalize to range [0..1).
			dval = (dval + 0.6f) / 2.3f;
			if (dval > 1.0f || dval < 0.0f) {
				Debug.Log("WARNING: got a distortion value outside my encoded range at pixel " +
				          i + ": " + distortion_data_[i]);
			}
			
			// Encode the float as RGBA.
			float enc_x = dval;
			float enc_y = dval * 255.0f;
			float enc_z = 65025.0f * dval;
			float enc_w = 160581375.0f * dval;
			
			enc_x = enc_x - Mathf.Floor(enc_x);
			enc_y = enc_y - Mathf.Floor(enc_y);
			enc_z = enc_z - Mathf.Floor(enc_z);
			enc_w = enc_w - Mathf.Floor(enc_w);
			
			enc_x -= 1.0f/255.0f * enc_y;
			enc_y -= 1.0f/255.0f * enc_z;
			enc_z -= 1.0f/255.0f * enc_w;
			
			int index = i >> 1;
			if (i % 2 == 0) {
				dist_pixelsX_[index].r = (byte)(256 * enc_x);
				dist_pixelsX_[index].g = (byte)(256 * enc_y);
				dist_pixelsX_[index].b = (byte)(256 * enc_z);
				dist_pixelsX_[index].a = (byte)(256 * enc_w);
			} else {
				dist_pixelsY_[index].r = (byte)(256 * enc_x);
				dist_pixelsY_[index].g = (byte)(256 * enc_y);
				dist_pixelsY_[index].b = (byte)(256 * enc_z);
				dist_pixelsY_[index].a = (byte)(256 * enc_w);
			}
		}
	}
	
	void ApplyDataToTextures() {
		main_texture_.SetPixels32(image_pixels_);
		main_texture_.Apply();
		
		distortionX_.SetPixels32(dist_pixelsX_);
		distortionX_.Apply();
		distortionY_.SetPixels32(dist_pixelsY_);
		distortionY_.Apply();
	}
}