# Plan: undo and redo, painting included

Status: implemented in GHistory.cs, except as noted here. Steps end when both
hands stop acting (GHand.Acting, or a UI drag), rather than with explicit
Begin/End calls; the budget is in bytes as planned. Decided with the owner: one global
queue for every kind of change, moving the world is never undoable, and the
buttons float to the left of the tool strip.

## One queue

Every change to the document goes into the same queue, in the order it
happened, whatever kind it was: move a vertex, then paint, and the first undo
removes the paint, the second puts the vertex back. Mesh edits and paint are
not separate histories, and there is no per-tool or per-hand undo.

## What a step is

One user action, from the moment it starts changing the document to the
moment it ends:

| Action | Starts | Ends |
|---|---|---|
| grab and move a part (with merges on release) | pinch on a part | release |
| extend (pull out a vertex/face) | middle pinch | release |
| delete a part | pinch | same frame |
| brush stroke | fingertip first paints | fingertip stops touching (lastBrush goes null) |
| paint bucket | first face filled | fingertip stops touching faces |
| texel size change (Settings) | slider grab | slider release |

Not steps: moving, turning or zooming the world (it's the view, never
undoable; Reset view exists), opening a file, New (those replace the
document; the old one stays in its file, and autosave covers the rest), the
name. Undo restores only the mesh and paint, and leaves the world where it is.

Two hands acting at once make one step each; their order is the order they
end in.

## How a step is stored: before-images

Each step keeps what the document looked like before it, only for the parts
it touched. Undo swaps the before-image in and keeps the after-image for
redo. No per-operation inverse code, so new tools get undo for free and there
are no inverse functions to get wrong.

- **Mesh**: a copy of the mesh structure, not the paint: the vertex, edge and
  face SwapLists (values, alive flags, free slots) and `faceCells`. Indices
  must come back exactly, since held parts and paint cells refer to them.
  That's about 50 bytes per vertex/edge/face: 1000 faces is ~150 kB, cheap
  enough to copy whole. Only copied when the step changed the structure
  (`ContentHash` without the paint version differs).
- **Paint**: the atlas pages split into 32x32 tiles. `GPaintAtlas` marks
  tiles dirty when it writes them (it already knows every write rectangle:
  PaintCapsule, Fill, Copy/Resample, Free/Allocate). At the first write to a
  tile during a step, the tile's old pixels (4 kB) are saved. A brush stroke
  across one face saves a handful of tiles, not the 1 MB page.
- **Allocator**: the buddy free lists, copied with the mesh (small).

Redo: undoing a step first saves the current contents of the same tiles and
the current mesh as the redo image. Any new step clears the redo list.

## Limits

A budget, not a count: keep steps until they total 64 MB on the Quest
(about 16k paint tiles), dropping the oldest. Log a warning the first time a
single step exceeds the whole budget (a texel size change over a big mesh
can), and keep it alone rather than refuse it.

## Where it lives

- `GHistory` (new), owned by `GDocument` since it belongs to that document:
  `Begin()`, `End()`, `Undo()`, `Redo()`, `CanUndo`, `CanRedo`.
- `GEditableMesh.Snapshot()` / `Restore(snapshot)`; `GPaintAtlas` gets a
  tile-dirty hook: `Action<int page, int tile>? beforeWrite`, set by
  `GHistory` while a step is open.
- `GHand` calls `doc.history.Begin()` / `End()` at the action boundaries in
  the table (the brush and bucket already track when touching starts and
  stops via `lastBrush` / `lastFilled`). A `Begin` while a step is open from
  the other hand nests: the step ends when both have ended.
- After Undo/Redo the hands drop what they hold (`StillThere` already handles
  a part that changed under them) and the renderer re-uploads the dirty pages.

Fail-loudly: `End` without `Begin`, `Undo` during an open step, and a
restored mesh that breaks an invariant all throw. The fuzzer gets a new op:
random undo/redo between actions, and checks that undoing everything returns
the starting `ContentHash` and redoing everything returns the final one.

## UI

- **Undo** and **Redo** buttons (curved arrow icons) in their own small
  window floating to the left of the tool strip, mirroring Home floating to
  its right, so the tools stay centered under the panel. Always there
  whatever the tool, one palm turn away. Dimmed when there's nothing to undo
  or redo. Built like the Home button (`GPalmMenu.DrawStrip`, `RowWidth(2)`,
  placed at `+(width / 2 + GAP + undoWidth / 2)` in window x).
- A quick gesture would be faster, but every comfortable gesture is taken and
  fist/clap poses misfire with tracking (plans/ui-design.md). Revisit with a
  palm-menu-free option if undo turns out to be used every few seconds: e.g.
  a double tap of the thumb on the side of the index while the other hand is
  open.
- Sound: a soft reverse "whoosh" for undo, forward for redo (plans/sound.md).

## Open questions

1. Should undo history survive saving and reopening? Not planned: it would
   need its own file, and the saved files are the long-term history.
