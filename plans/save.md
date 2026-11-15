# Plan: Save / load files

Status: implemented. Decisions from review: autosave yes,
thumbnail inside the file (one file per save), app-specific folder.

## Goals

- Save the current mesh to a standard format other programs can import.
- Each save has a thumbnail of what the user was looking at when they saved.
- Files are easy to get off the Quest.
- A "Files" tab on the palm menu shows and edits the current file's name,
  saves, and lets you browse and open existing files.

## File format: glTF binary (`.glb`)

glTF is the standard interchange format (Blender, Unity, Godot, three.js,
Windows 3D Viewer all import it). It's one self-contained file that can also
hold the paint texture later (see `paint.md`), and StereoKit can already render
`.glb` files if we ever want previews.

A save is one file, `<files dir>/my_shape.glb`, with the thumbnail inside it
(see Thumbnail). One file means the thumbnail can't get separated from or out
of date with its mesh.

### Why not 3MF?

3MF does have a standard package thumbnail (`/Metadata/thumbnail.png`), and
it supports per-triangle colors and textures. But it's made for 3D printing:

| | glb | 3MF |
|---|---|---|
| Thumbnail | no standard slot, but an embedded image is easy to read back | standard |
| Blender | built in | third-party add-on |
| Unity / Godot / three.js / Windows 3D Viewer | yes | some (three.js loader, 3D Viewer), not game engines |
| Loose edges (wireframe parts) | `LINES` primitive | no, triangles only |
| Textures (paint) | `baseColorTexture` | texture extension, spotty support |
| App data | `extras` | custom metadata |

Losing loose edges and Blender import is worse than an unofficial thumbnail
slot, so glb it is, with the thumbnail embedded as an image (below).

### What goes in the glb

One node, one mesh, with up to two primitives:

| Primitive | Mode        | Contents                                                     |
|-----------|-------------|--------------------------------------------------------------|
| 0         | `TRIANGLES` | `faces`, winding order preserved, so backfaces stay backfaces |
| 1         | `LINES`     | Only *loose* edges, ones that aren't the side of any face     |

Both primitives share one `POSITION` accessor holding `vertices`. Edges that
belong to a face aren't written as lines, otherwise Blender and others would
import them as duplicate loose edges.

Some editor state has no standard glTF equivalent, so it goes into the mesh's
`extras` (glTF's sanctioned place for app data, other programs ignore it):

```json
"extras": {
  "gesh": {
    "version": 1,
    "edges": [[0,1],[1,2], ...],      // full edge list, including face edges
    "world": [px,py,pz, rx,ry,rz,rw, s], // GWorld, so reopening restores the view
    "thumbnail": 0                      // index of the thumbnail image
  }
}
```

On load, if `extras.gesh` exists, use it as the source of truth for edges.
Otherwise (a glb from another program), rebuild edges from the triangle sides
plus the `LINES` primitive, and merge vertices that share a position. So any
glb with triangles can be opened and edited, not just our own.

The `SwapList` free slots are compacted on save, with indices remapped, so
the file never contains dead vertices.

### Library

Use **SharpGLTF.Core** (MIT, NuGet, pure C#, works on Android). It handles
the glb container, accessors and `extras` and validates output. Hand-writing
the glb JSON and binary chunk would only be ~200 lines, but loading arbitrary
third-party glbs robustly (sparse accessors, strides, different index types)
is where a library pays off.

## Where files live, and getting them off the Quest

| Platform | Folder |
|----------|--------|
| Quest    | `/sdcard/Android/data/com.graic.gesh/files/gesh/` from `Context.GetExternalFilesDir(null)` |
| Desktop  | `Documents/gesh-files/` from `Environment.SpecialFolder.MyDocuments` (not `Documents/gesh`, which is where the source lives) |

Pick the folder in one platform-specific function, `GFiles.Folder()`, and
keep everything else platform independent.

The app-specific external directory needs **no permissions**, never has
scoped-storage problems when listing or reading our own files, and survives
app updates (but not uninstalls). Getting files off the device:

1. **USB:** plug the Quest into a PC, allow file access in the headset, and
   browse to `Quest 3/Internal shared storage/Android/data/com.graic.gesh/files/gesh`.
2. **adb:** a `just pull` recipe:
   `adb pull /sdcard/Android/data/com.graic.gesh/files/gesh ./saves`.
3. Optional later: a "Share" button that fires an Android share intent
   (`ACTION_SEND` with a `FileProvider` URI) to send a file to Meta's
   phone app, Drive, etc. This is the only path that doesn't need a PC, but it
   adds a `FileProvider` manifest entry and Java interop, so it's not in v1.

Alternative: write to the public `Documents/gesh` folder through
`MediaStore`. It's easier to find in the Quest's Files app, but after a
reinstall the app loses ownership of old files and needs
`READ_MEDIA_*`/SAF permission flows to list them again. Not worth it for v1.

## Thumbnail

On save, render the view from the head pose with the `Renderer.Screenshot`
callback overload, 256x256:

- Mesh, floor and handles draw on `RenderLayer.Layer1`, and the screenshot's
  `layerFilter` only includes that, so the palm menu (which the user is
  looking at while pressing Save) isn't in the shot.
- The callback runs at the end of the frame with raw RGBA pixels. Encode them
  to PNG with a small managed encoder (`GPng`, zlib from
  `System.IO.Compression`, no dependency), then write the glb. A save lands
  one frame after pressing Save.
- The PNG goes into the glb as an image no material uses, with
  `extras.gesh.thumbnail` pointing at it. Other programs ignore it.
- Reading it back: the glb's image bytes, `Tex.FromMemory`, `Sprite.FromTex`.

## Palm menu: "Files" tab

Adds `Files` to `GMenuTab`. Layout, top to bottom:

```
[ Settings | Files ]
------------------------------
Name: [ my_shape          ]      UI.Input, opens the system keyboard
[ Save ]  [ New ]
------------------------------
 ┌─────┐ ┌─────┐ ┌─────┐
 │thumb│ │thumb│ │thumb│         grid of existing files, newest first
 └─────┘ └─────┘ └─────┘         label under each: name + date
  name    name    name
 ...                             paged, 9 per page: [<] 1/3 [>]
```

- **Name:** the current file's name, default `gesh_YYYY-MM-DD_HHMM`. Characters
  invalid in file names are stripped when saving.
- **Save:** writes `<name>.glb`, thumbnail included. If the name already exists
  and isn't the currently open file, the button turns into "Overwrite?" for a
  second press rather than opening a dialog.
- **New:** clears to the starting triangle with a fresh default name.
- **Tapping a thumbnail** opens that file and sets the name field to its
  name. If there are unsaved changes, the first tap shows "Discard changes?"
  in place, same as Overwrite.
- Long press or a small trash button per tile deletes, with the same
  confirm-in-place pattern.

Thumbnails load from each glb's embedded image → `Tex.FromMemory` →
`Sprite.FromTex`, cached by path and modified time, and only for the visible
page, so big folders stay cheap.

## Autosave

- Every 30 seconds, and when the app is paused or quit, if the mesh changed
  since the last save or autosave, write `autosave.glb` (overwritten each
  time, no thumbnail, so it costs no extra frame).
- "Changed" is the same hash comparison as unsaved changes, no dirty flag.
- On startup, if `autosave.glb` is newer than every other file, load it, so a
  crash doesn't lose work.
- It shows in the browser as "autosave" like any other file, so it can be
  opened or saved under a real name.

## Code structure

Keep it to plain functions plus one small state object, following the
existing style:

```
GFiles.cs
  static string Folder()                                  platform specific
  static void Save(GEditableMesh mesh, Matrix world, string path)
  static (GEditableMesh mesh, Matrix world) Load(string path)
  static List<GFileEntry> List()                          name, paths, modified time
  static string SanitizeName(string name)

GPalmMenu.cs
  + GMenuTab.Files
  + DrawFiles(...)            UI only, calls GFiles
```

State that has to live somewhere:

- `currentName` (string) – lives in the menu, it's purely UI.
- "Unsaved changes": don't track a dirty flag (that's a second source
  of truth that can drift). Instead keep a hash of the last saved/loaded
  mesh data and compare against the current mesh when the question comes up.
  Cheap at our mesh sizes, and always correct.

`GEditableMesh` gets no file code; `GFiles` reads and writes its public
`vertices`, `edges` and `faces`. Load returns a new mesh that `Program`
swaps in, so `GEditableMesh` stays construct-and-edit only.

## Testing

- Round trip: build a mesh with loose edges, backfaces, and freed SwapList
  slots → save → load → compare topology and positions.
- Import a glb made in Blender (with and without our extras) and check it
  turns into an editable mesh.
- Export, then import into Blender: faces face the right way, loose edges
  show up as edges, no duplicate edges.
- On device: save, plug in USB, find the file, open it on PC.

## Decisions

1. App-specific folder: losing saves on uninstall is fine for v1.
2. The world transform is saved, reopening restores the view.
3. Autosave: yes, see above.
4. Thumbnail embedded in the glb (3MF considered, see above).

Performance work follows `perf.md`.
