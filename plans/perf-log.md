# Performance log

Every attempt from the hill climb in `perf.md`, kept or not. Measured with
`just bench` on the connected Quest 3: Release build, CPU and GPU clock
levels locked at 2, nobody wearing it. 1000-face scene (about 1500 edges,
500 vertices) that turns, moves a vertex and paints every frame, 30 seconds
measured after 10 of warmup.

- **cpu** is the app's own managed time per frame (Stopwatch around the frame
  callback), median and 95th percentile.
- **gpu** is the runtime's `App` time per frame (VrApi).
- Run to run noise on the baseline was about 0.01 ms (median) and 0.2 ms (p95).

| # | Change | cpu p50 | cpu p95 | worst | gpu | Kept |
|---|--------|---------|---------|-------|-----|------|
| 0 | Baseline | 4.15 | 9.2 | 35 | 1.73 | |
| 1 | `SwapList` loops: struct enumerators instead of LINQ (`Range().Where()`) | 3.80 | 9.1 | 35 | 1.73 | yes |
| 2 | No boxing `Vec3.Equals` in `MoveVertex`'s overlap check and the brush; brush visits only the pixels it can reach, without a per-pixel delegate | 3.28 | 6.5 | 12 | 1.73 | yes |
| 3 | Brush skips faces whose bounding box misses the stroke's | 2.42 | 5.6 | 11 | 1.74 | yes |
| 4 | Vertex handles: one mesh of camera facing quads shaded as spheres (`vertex.hlsl`) instead of a `Mesh.Sphere` draw per vertex; face and edge building read vertices in place (`CollectionsMarshal.AsSpan`) | 2.21 | 5.5 | 11 | 1.09 | yes |
| 5 | Full AOT compilation for Release (`RunAOTCompilation`) | 2.23 | 5.5 | 11 | 1.10 | no, no difference |
| 6 | 120 Hz display refresh rate (`GRefreshRate`, was the 72 Hz default) | 2.23 | 4.6 | 11 | 1.09 | yes, 120 fps with 0 stale frames |

Notes:

- The worst frames in the baseline (~35 ms) were garbage collections: change 2
  removed a boxing allocation per vertex per frame and per brush pixel.
- Change 1 was smaller than expected, the per-frame cost on Mono is mostly
  per-element work, not the iteration itself.
- At 120 Hz the budget is 8.3 ms. A 3000-face scene measured 4.7 ms median,
  6.1 ms p95, and held 120 fps except for ~1.5% of frames. Meshes beyond
  roughly 5000 faces would start missing frames at 120 Hz.
- **Debug vs Release:** the same scene in a Debug build measured 9.2 ms median
  (edges alone 4.2 ms). `just play` used to deploy Debug, it now deploys
  Release (`just play-debug` for debugging).
- Startup bug found while benching (not perf): StereoKit converted the Android
  window surface on its own thread after it could already be released, and
  crashed on quick relaunches. `MainActivity` now converts it right away
  (`SK.SetNativeWindow`, added to our StereoKit fork).

Where the time goes now (1000 faces, per frame): edges 0.8 ms, building the
face meshes 0.6 ms, brush 0.6 ms (only while painting), paint page upload
0.25 ms (only when paint changed), vertex spheres 0.2 ms.

## Next candidates

- **Skip rebuilding the face, edge and vertex meshes on frames where the
  mesh didn't change** (a change counter on the mesh). That's ~1.6 ms of the
  2.2 ms on idle frames at 1000 faces. Not done: it adds state every edit has
  to keep right, and the user preferred reusing memory over a dirty flag, so
  this needs their go-ahead.
- Edge and vertex index buffers never change pattern; they could be filled
  once and only grown.

## Paint resolution

Paint cells used to be a fixed 64x64 per face, so big faces were very
pixelated. Cells are now sized by the face (`GPaintAtlas.texelSize`, a
power of two from 16 to 512 pixels, packed with a buddy allocator into
512x512 pages; pages stay small since StereoKit can only upload whole
textures). Same 1000-face bench, 120 Hz:

| Texel | Paint memory | Extend a 1 m face | cpu p95 | Brush | Kept |
|-------|--------------|-------------------|---------|-------|------|
| 2 mm | 25 MB | 81 ms | 10.7 (drops frames) | 1.43 | no |
| 3 mm | 11.5 MB | 19 ms | 6.0 | 0.92 | yes, default |
| 4 mm | 8.4 MB | 19 ms | 5.7 | 0.85 | no, coarser for no gain |
| 3 mm, resample rows spread over cores (`Parallel.For`) | 11.5 MB | 7.3 ms | 6.0 | 0.91 | yes |

2 mm crossed a power of two for the faces made by extending a 1 m face
(512 pixel cells instead of 256), four times the resample work. Also kept:
an integer bilinear sample, and skipping the blank fill for cells that are
resampled into right away. The resolution is per document and can be
changed in the Experimental tab, which refits every face.
