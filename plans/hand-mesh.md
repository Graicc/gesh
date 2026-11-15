# Meta's hand mesh

Status: implemented in `GMetaHandMesh.cs`, behind Home > Settings >
Experimental > Meta hand mesh (on by default). The extension activates on the
Quest 3; how it looks with real hands is unchecked. It uses StereoKit's
default material, not the hand material, double sided.

Can we draw Meta's own hand mesh (the one the system menus use) instead of
StereoKit's generated one? Yes: the Quest offers `XR_FB_hand_tracking_mesh`
(the startup log lists it as present), and StereoKit has everything needed
to draw it. StereoKit itself doesn't use the extension, so it's ours to add.

## How

A stepper like `PassthroughFBExt`, registered before `SK.Initialize`:

1. Request `XR_FB_hand_tracking_mesh` with `Backend.OpenXR.RequestExt`.
2. After initialize, create our own `XrHandTrackerEXT` per hand
   (`xrCreateHandTrackerEXT`, alongside StereoKit's: the runtime allows
   several) and call `xrGetHandMeshFB` twice, once for the counts and once
   for the data. That gives, once per hand:
   - vertex positions, normals, UVs, and 4 joint indices and weights each;
   - the triangle indices;
   - each joint's bind pose and radius.
3. Build a StereoKit `Mesh` from it, with `Mesh.SetSkin(jointIds, weights,
   inverse bind poses)`.
4. Each frame, `xrLocateHandJointsEXT` on our tracker, in
   `Backend.OpenXR.Space`, gives the 26 joint poses in OpenXR's joint order,
   which is the order the mesh's joints use. Pass them to `Mesh.UpdateSkin`
   and draw it with the hand material. Using our own joint query keeps the
   joint order and orientations exactly OpenXR's, instead of mapping
   StereoKit's `Hand` joints back.
5. `Input.HandVisible(Handed.Max, false)` hides StereoKit's hand. If the
   extension is missing, or the hand isn't tracked, don't hide it.

About 150 lines, all in one file.

## What it changes

- The hands look like the system's hands, and match the user's real hand
  size better (the mesh is scaled to the tracked hand).
- It's a better occluder for MR phase 2 (`plans/mr.md`): drawn depth only
  over passthrough, it covers the model where the real hand is, more exactly
  than StereoKit's capsule-ish hand.
- StereoKit's hand mesh also shows the pinch state by its color fading; the
  hand material keeps doing that if we reuse it.

## Risks

- Can only be checked with hands in the headset; the headless harness has
  no hands.
- If the joint poses aren't in the same space the mesh is drawn in, the
  hand appears offset; check with the world moved and scaled (the hands are
  drawn in real space, not the model's).
