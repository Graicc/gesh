# Mixed reality

Status: phase 1 implemented: `PassthroughFBExt` (copied from the StereoKit
examples) registered in `Program.cs`, a Passthrough toggle in Home >
Settings (on by default), settings saved to `settings.json` by
`GSettingsFile`, the floor hidden over passthrough, and the passthrough
splash in the manifest. The `Device.ValidBlend` check was skipped:
`XR_FB_passthrough` is there on the Quest, and is the known route. Mesh
colors are unchanged (black edges and vertices, white faces) until they've
been looked at against a real room. Phases 2 to 4 are still plans.

Model in your room instead of in the void. The model sits on your real desk,
your real hands cover it when they're in front, and it's still there tomorrow.

## What's there already

- The Android manifest already declares `com.oculus.feature.PASSTHROUGH`
  (not required) and `com.oculus.permission.USE_SCENE`.
- `Program.cs` draws the floor grid only when `Device.DisplayBlend` is
  `Opaque`, so it already hides itself over passthrough.
- The StereoKit fork has:
  - `Device.DisplayBlend` / `Device.ValidBlend` and `SKSettings.blendPreference`;
  - an example `PassthroughFBExt` stepper (`stereokit/Examples/StereoKitTest/Tools/PassthroughFBExt.cs`, MIT) for
    `XR_FB_passthrough`, which is how Quest does passthrough;
  - `Sensor.Depth` and `World.Occlusion` on `XR_META_environment_depth`;
  - spatial entities (`SpatialEntity.cs`) for anchors.

## Phase 1: passthrough on and off

- Copy `PassthroughFBExt` into the project and register it with
  `SK.AddStepper` before `SK.Initialize`. First check whether
  `Device.ValidBlend(DisplayBlend.Blend)` is true on the Quest 3 by itself:
  newer Meta runtimes support the alpha-blend mode directly, and then the
  extension isn't needed. Use the simplest one that works.
- Add a **Passthrough** toggle to Home > Settings, on by default when it's
  available. Settings aren't saved between runs yet, so this needs
  `GSettings` saved too (a small JSON next to the saves). That's useful for
  the pinch thresholds anyway.
- Add `<meta-data android:name="com.oculus.ossplash.background"
  android:value="passthrough-contextual"/>` so launching doesn't flash black.
- Check readability against a real room:
  - Edges and vertices are light gray and blue now, which a white wall
    washes out. Give them a thin dark outline, or darken them a bit while
    passthrough is on.
  - Unpainted faces are near white; same problem, same fix.
  - The menus are opaque panels already, so they're fine.
- Thumbnails and `.glb` export render only `GSaver.SCENE_LAYER`, so
  passthrough doesn't leak into them. Confirm a thumbnail taken in MR has the
  usual background.

## Phase 2: hands in front of the model

Over passthrough, the model draws over your real hands, which feels wrong
fast. Two options, cheapest first:

1. **Hand mesh as an occluder.** Draw StereoKit's hand mesh with a depth-only
   material (writes depth, no color) before the model. Exact for hands,
   nothing else. That's a few lines: the hand material swapped for one with
   no color writes.
2. **Environment depth** (`World.Occlusion |= OcclusionCaps.Depth`, Quest 3
   and later). It covers everything (hands, the desk, the chair), but the
   edges are soft and low resolution. Costs GPU time; measure with
   `just bench`.

Start with 1. Try 2 behind a setting and keep it only if it looks right
around the fingertips, which is where people look.

## Phase 3: the model stays where you left it

The document already saves `GWorld` (where the model is relative to the
tracking origin), but the origin moves after a recenter or a new boundary, so
the model jumps.

- On save, create a spatial anchor at the model's origin and store its id in
  the document. Store `GWorld` relative to that anchor instead of relative to
  the tracking origin.
- On open, look up the anchor. If it's found, place the model on it. If it
  isn't (another room, or anchors were cleared), fall back to the current
  placement, in front of the user.
- Anchors are per device. The `.glb` doesn't need them, so they go in the
  extras block with the paint data, and other programs ignore them.

## Phase 4, later: the room as a reference

- Scene planes (`USE_SCENE` is in the manifest already) let a released
  vertex snap to a real table or wall, and give a "put it on the desk"
  button.
- Shadows on real surfaces (a soft blob under the model, on the floor plane)
  help a lot with judging where it sits. Cheap once planes exist.

## Not doing

- Passthrough camera access for picking colors off real objects. Meta allows
  it now, but it needs another permission and a privacy prompt. Revisit if
  people ask.

## Testing

None of this runs in the headless harness. Checklist on the headset:

- toggling passthrough from Settings, both ways, with the menu open;
- the floor hides and comes back;
- the model is readable against a white wall and a dark floor;
- a hand in front of the model covers it;
- save, recenter, reopen: the model is where it was.
