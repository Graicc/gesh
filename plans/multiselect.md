# Plan: selecting several parts and moving them together

Status: implemented as below (Select tool, sweep to add, start on a selected
part to remove, Edit grabs the whole selection, merging on release only into
unselected vertices). The selection lives on GEditableMesh rather than
GDocument, since RemoveVertex has to drop removed vertices from it.

Added since: with the Select tool, pinching the model with both hands moves,
rotates and scales the selection around the point between the hands
(`GTwoHandGrab`, the world navigation math, scale sticking at 100%). The
two pinches are a grab, not sweeps, so the selection goes back to how it was
when the first one started. Pinching the air still moves the world. A double pinch in the air clears the selection and switches to
Edit. Not done: select connected.

## What the selection is

A set of **vertices**. An edge or face counts as selected when all its
vertices are. One source of truth, and it survives everything the mesh does:
moving a selection moves vertices, merges remove vertices (drop them from the
set), extends add vertices (not selected). Stored on `GDocument`, not the
mesh, since it's editor state that isn't saved.

## Selecting: sweep with a select pinch

Add **Select** as a second tool in the Edit tab, next to the current
grab/extend tool (icons: hammer, and a dashed box or lasso).

- **Pinch and sweep** over parts to add them, like the fill sweep in paint:
  every vertex, edge or face the pinch point passes within reach of gets its
  vertices added. Feels like painting a selection, works for scattered parts
  and for runs of faces.
- A pinch that **starts on an already selected** part removes instead, for the
  whole sweep, so you can paint a selection off again.
- A select pinch that starts in **empty air** still moves the world, same as
  every tool.
- **Clear** is a button in the Edit tab, and switching back to the edit tool
  keeps the selection so it can be moved.

Why not a box: a 3D box needs two hands and a depth, is hard to aim around a
shape you're standing next to, and selects hidden backside parts you didn't
mean to. A sweep only selects what your hand actually touches.

## Moving the selection

With the edit tool, **grabbing any selected part grabs the whole selection**:
every selected vertex keeps its offset from the hand, and follows the hand's
position and rotation, exactly like a held face moves its three corners now
(`GHand` already moves a list of held vertices with offsets, so this is the
same code with a longer list).

On release:
- Merging: each selected vertex merges into a vertex it was dropped on, like
  the corners of a held face, but only into **unselected** vertices.
- Paint cells refit for the moved faces, as now.

Grabbing an unselected part works as today and doesn't touch the selection.
Extending (middle pinch) ignores the selection.

## Showing it

Selected vertices and edges draw in the accent blue instead of black, and
selected faces get a light blue tint. Cheap: a color per vertex in the vertex
and edge meshes, which are rebuilt every frame anyway.

## Two-handed extras, later

Once grabbing a selection works, a second hand pinching the same selection
could **scale and rotate** it around the point between the hands, the same
math as the two-hand world navigation in `GWorldNav`.

## Alternatives considered

- **Tap to toggle** each part: precise, but slow for many parts. The sweep
  covers it (a sweep that barely moves is a tap).
- **Box / sphere volume select** with both hands: good for "everything in
  this region", worth adding later as a second select mode if needed.
- **Select connected**: double pinch to flood fill a selection across
  connected faces. Useful, small to add on top of the vertex set.

## Open questions

1. Should merging on release be on for selections at all? Dropping a large
   selection near the rest of the mesh could merge a lot of vertices at once.
   Maybe only when the selection is small, or never.
2. Does the selection clear when the menu's tool tab changes to Paint?
3. Your thoughts on the select gesture itself.

## Answers from user

1. yes it should be on
2. no, and it should restrict painting to only the selected faces
3. gesture seems good
