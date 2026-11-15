# Plan: Painting faces

Status: implemented (see "As built" first, it changed one big decision). Depends on the save plan (`save.md`)
for storage. Decisions from review are at the bottom.

## As built: paint lives on the CPU

The design below paints on the GPU. While implementing, that changed to
painting on the CPU, keeping everything else (cells, resampling, tools, tab,
saving) the same:

- `GPaintAtlas` (GPaint.cs) holds the pixels in plain byte arrays, split into
  512x512 pages of 64 cells each. Pages are added as faces need cells, so
  there's no face limit.
- Brush, fill and resample are C# loops over a cell's 64x64 pixels. A stroke
  touching ten faces is ~40k pixels, cheap even on the Quest.
- The renderer draws one mesh per page with that page as its texture, and
  re-uploads a page only when its version changed. A page is 1 MB.
- A face flipping toward the viewer transposes its cell's pixels, so paint
  stays put when corners b and c swap. Merged faces carry their cell along.

Why: the GPU design needed render-to-texture with a dedicated layer,
backend dependent UV flips, GPU readback to save, and GPU upload to load.
On the CPU, paint is just more mesh data: one source of truth, saved and
loaded with no GPU involved, and the headless fuzzer and tests cover it
(resample, flip, merge, save/load round trip, cell leak checks). The cost is
the page upload while painting, which perf.md measures.

The gutter trick from below is simpler too: the CPU rasterizes a couple of
pixels past the triangle's edges directly, no triangle expansion needed.

## Goals

- Paint on faces with a **brush**: a sphere on the index fingertip, shown in
  the current color, with an adjustable size. Wherever the sphere overlaps
  the mesh gets painted.
- A **fill** tool that paints a whole face at once.
- A "Paint" tab on the palm menu for picking the tool, color and size.
- The paint is saved in the mesh file and shows up in other programs.

## Core idea: one texture, one atlas cell per face

Vertex colors can't hold brush strokes (a triangle only has 3 colors), so
paint lives in a texture. That needs UVs, and the mesh is constantly changing
topology, so a real unwrap is out. Instead each face gets its **own fixed
cell** in a texture atlas:

```
atlas 2048 x 2048, cells 64 x 64 px  ->  32 x 32 = 1024 cells
each cell holds one face's triangle:

  +--------+      corners: (0,0) (1,0) (0,1) of the cell, inset by a
  |\       |      gutter of 2 px on each side so bilinear filtering and
  |  \     |      mipmaps don't bleed neighbors in
  |    \   |
  | tri  \ |
  +--------+
```

(Two triangles per cell would double capacity, but they'd share a diagonal
and bleed into each other. One per cell is simpler; 1024 faces is plenty
for now, and the atlas can grow to 4096² = 4096 faces.)

- The face → cell mapping is **stored on the face**: `GFace` gets an `int cell`.
  It's part of the face's data, so there's one source of truth, and it moves
  with the face through `SwapList` reuse.
- Free cells: a `Queue<int>` like `SwapList.free`. On `AddFace` take a free
  cell and clear it to white; on `RemoveFace` return the cell.
- Moving vertices stretches the texture with the face, like any textured
  model. That's the expected behavior.

### What happens to paint during edits

| Edit | Paint result |
|------|--------------|
| Move vertex/edge/face | Paint stretches with the face |
| Extend a face into 3 | The old paint is **resampled** into the 3 new faces (below) |
| Extend an edge | New face is blank (white) |
| Merge (face remapped) | `ReplaceVertex` keeps `cell`, paint is kept |
| Merge (faces cancel) | Both cells freed |

## Rendering

The render mesh can no longer share vertices between faces: a vertex has
different UVs in each face's cell. `GEditableMesh.UpdateMesh` already rebuilds
the GPU mesh every frame, so it now emits **3 vertices per face** (unwelded)
with the cell UVs. The editable topology (`vertices`, `edges`, `faces`) is
unchanged, only the GPU copy is expanded. At our sizes this is cheap.

`mesh.hlsl` already samples `diffuse` × vertex color, so the atlas just gets
set as the material's `diffuse`. The backface pass is unchanged.

The atlas is a `Tex` created with `TexType.Rendertarget | TexType.ImageNomips`,
`TexFormat.Rgba32`, cleared to white.

## Painting on the GPU (original design, replaced, see As built)

Painting runs in **UV space**: draw the affected faces into the atlas, with
their vertices placed at their UV coordinates instead of their 3D positions,
and test each pixel against the brush sphere in 3D.

`Assets/paint.hlsl`:

```hlsl
// vertex: put the triangle where its cell is in the atlas, but keep the
// 3D position (mesh space) for the pixel shader
o.pos   = float4(input.uv * 2 - 1, 0, 1);   // (flip y if needed per backend)
o.local = input.pos.xyz;

// pixel: soft-edged sphere
float d = distance(input.local, brush_center.xyz);
float a = saturate((brush_radius - d) / (brush_radius * hardness_falloff));
return float4(brush_color.rgb, brush_color.a * a);     // alpha blended
```

Each frame while painting:

1. **CPU:** find faces within `brushRadius` of the brush center using
   `Math.DistanceToTraingleSq`, which already exists for grabbing.
2. Build a small mesh of just those faces (3 verts each, with cell UVs).
3. `Renderer.RenderTo(atlas, ...)` with `RenderClear.None` and a render layer
   only the paint draw uses, so the scene isn't drawn into the atlas.
   (`Renderer.Blit` doesn't take a mesh, so `RenderTo` + a dedicated layer
   is the way to draw arbitrary geometry into a target.)
4. **Gutters:** rasterization only covers pixels whose centers are inside the
   triangle, so edge texels would be left unpainted and show seams. Expand
   each triangle in UV space by ~1.5 px (push each corner away from the
   centroid) before drawing. The clamp-to-cell gutter keeps that from
   reaching neighbor cells.

**Resampling on face extend:** extending a face replaces it with three
faces that together cover the old triangle at the moment of the extend (the
new vertex starts at the pinch point, on or near the old face). So the old
paint can be carried over:

1. Before removing the old face, copy the atlas into a scratch texture with
   `Renderer.Blit` (a texture can't be read and drawn to at once).
2. Draw each new face into its cell with `resample.hlsl`: the same UV-space
   vertex shader as painting; per pixel take the 3D point, find its
   barycentric coordinates in the *old* triangle (projected onto its plane,
   clamped to it), and sample the scratch copy at the old cell's UV there.
3. Free the old cell.

If the new vertex was pinched off the face plane, the projection squashes the
paint slightly toward it, which is fine.

**Fill tool:** same pipeline, with `brush_radius = infinity`, drawn once
for the one face under the fingertip on pinch.

Stroke smoothing: at hand-tracking speeds the fingertip can jump more than a
brush radius between frames, leaving dots. Draw the sphere at several points
interpolated between last and current position (spacing ≈ radius / 2). The
last position is the only extra state; it lives in the paint tool and is
cleared when a stroke ends.

The brush is pure GPU work after a cheap CPU face search, so cost is
independent of atlas size.

## Interaction

A **tool** setting decides what the hands do: `Edit` (today's behavior),
`Brush`, or `Fill`. It's stored in `GSettings.tool`, set from the Paint tab,
and passed to `GHand`, which already receives settings.

- **Brush:** a sphere of `brushRadius` (irl) is drawn at the index fingertip
  in the current color, translucent. When it **overlaps the mesh** it paints
  (finger painting, no pinch needed). The mesh editing grabs are disabled in
  this mode, so poking into the mesh is safe. Because the sphere is in irl
  units and the mesh is in mesh space, the radius is converted the same way
  as the grab radius: `brushRadius / world.Scale.x`.
- **Fill:** the fingertip shows a small sphere in the current color; an
  index pinch paints the closest face (`GetClosestPart` limited to faces).
- Air pinch navigation still works in every mode, since air pinches are
  exactly the pinches that don't hit the mesh.
- The menu hand never paints (it's showing the palm).

## Palm menu: "Paint" tab

```
[ Settings | Files | Paint ]
------------------------------
Tool:  (Edit) (Brush) (Fill)
Size:  [-----o-------]  2.0cm        brush only
Color: ■ ■ ■ ■ ■ ■ ■ ■               palette, 16 swatches
       H [------o----]
       S [----------o]               HSV sliders edit the current color
       V [--------o--]
       [ preview swatch ]
```

`GSettings` gets `tool`, `brushRadius`, `brushColor`. The palette is a
constant array, and choosing a swatch just sets `brushColor`.

## Saving the paint

From `save.md`, the glb gets:

- `TEXCOORD_0` per vertex. glTF wants per-vertex UVs, so the triangle
  primitive is written **unwelded** (3 verts per face), the same as the
  render mesh.
- A material with `baseColorTexture` = the atlas, encoded as PNG.
- `extras.gesh` gains `faceCells: [int per face]` and a `vertexIndex` array
  mapping each unwelded vertex back to the original vertex index. On load,
  that rebuilds the exact shared-vertex topology and cell assignment, so
  splitting vertices for export doesn't break editing.

Readback: `atlas.GetColorData<Color32>()` → PNG encode. StereoKit's
`Tex` has no PNG encoder in the C# API, so use a small managed encoder
(StbImageWriteSharp, public domain) or SharpGLTF's image helpers. Reading
back a 2048² texture takes a few ms, which is fine on save but not every frame.

On load: decode the PNG and `atlas.SetColors(...)`, then rebuild the free
cell queue from the cells not used by any face.

Other programs (Blender etc.) will show the paint correctly, since it's a
plain base color texture with UVs. They'll see an odd-looking atlas if
they open the texture itself, which is fine.

## Code structure

```
GEditableMesh.cs   GFace.cell, cell allocation in AddFace/RemoveFace,
                   unwelded UpdateMesh, owns the atlas Tex
GPaint.cs          brush/fill: face search, building the paint mesh, RenderTo,
                   stroke interpolation (lastBrushPos)
Assets/paint.hlsl  UV-space sphere painting
GPalmMenu.cs       Paint tab
GSettings.cs       tool, brushRadius, brushColor
GFiles.cs          UVs, texture, faceCells/vertexIndex extras
```

## Risks / things to verify first

1. **`RenderTo` into a texture mid-frame** with a custom layer and
   `RenderClear.None`: check the atlas keeps its contents across frames
   on the Quest's tiled GPU. If it doesn't, fall back to ping-ponging two
   targets with `Renderer.Blit`. Prototype this first, it's the riskiest piece.
2. **UV-space y flip** differs between backends; check on Vulkan desktop and
   Quest with one test stroke.
3. **Atlas capacity:** 1024 faces at 2048². If modeling sessions routinely
   exceed that, grow the atlas by blitting into a bigger one and rescaling
   all cell UVs (cells keep their index, only the cell size changes).

## Decisions

1. Extending a face resamples the old paint into the 3 new faces.
2. Still open, using the plan's defaults until decided: paint on touch, the
   eraser is "paint white", uniform 64px cells.

Performance work, including measuring the paint pass, follows `perf.md`.
