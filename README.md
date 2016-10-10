LeapIsolatedHands
=====================

A Unity demo demonstrating hand isolation and display from the raw video passthrough.  Push and pull floating blocks with your real hands (tinted to a flesh tone of course).  

Ideally suited as a base for further experiments.


Code in non-optimized, and the recursive flood-fill segmentation technique has proven to be too slow for use in a shippable application.  Ideally, camera data would be projected from the camera's position directly onto the hand meshes inside of the vertex shader (netting you segmentation and stereo-correct reprojection in one, cheap operation).

Video:

[![Fine Interactions with Isolated Hands](http://img.youtube.com/vi/c8uZHaoZl_w/0.jpg)](http://www.youtube.com/watch?v=c8uZHaoZl_w)

This demo's page on Leap Motion's Gallery of Examples:

https://developer.leapmotion.com/gallery/hand-background-isolation
