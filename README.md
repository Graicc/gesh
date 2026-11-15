# gesh

Hand-tracked mesh modeling and painting for Meta Quest, built on a vendored
fork of [StereoKit](https://stereokit.net). Pinch to grab and pull out
vertices, edges and faces, paint them, and save them as `.glb` files that
other 3D programs open.

## Setup (Windows)

Install:

- [.NET 10 SDK](https://dotnet.microsoft.com/download), with the Android
  workload: `dotnet workload install android`
- Visual Studio 2022 with **Desktop development with C++** (StereoKit's native
  code and shader compiler are built from source in `stereokit/`)
- [CMake](https://cmake.org) 3.21+
- [just](https://github.com/casey/just): `winget install Casey.Just`

For the headset, also:

- [ninja](https://ninja-build.org): `winget install Ninja-build.Ninja`
- Android NDK r25c (Android Studio's SDK Manager, or
  [the NDK downloads](https://developer.android.com/ndk/downloads/revision_history)),
  with an `NDK` environment variable pointing at it:
  ```powershell
  [Environment]::SetEnvironmentVariable('NDK', 'C:/Users/<you>/AppData/Local/Android/Sdk/ndk/25.2.9519653', 'User')
  ```
- The Android SDK platform `android-32`, and `adb` on your PATH (comes with
  the Android SDK platform-tools)

Restart your terminal afterwards so it picks up the new PATH and `NDK`. The
first build of each platform compiles StereoKit and takes a few minutes.

## Running

On the desktop, with StereoKit's simulator (or a PC VR headset):

```
just run
```

On a Quest:

1. Turn on developer mode for the headset (in the Meta Horizon phone app,
   under the headset's settings), connect it with USB, and allow USB debugging
   when the headset asks. `adb devices` should list it.
2. Build, install and start it:
   ```
   just play
   ```
   This is a Release build: Debug runs about 4x slower on the headset. Use
   `just play-debug` to attach a debugger.

Saved models are in the app's files folder on the headset;
`just pull` copies them into `./saves`. Settings are saved there too, in
`settings.json`; delete it to go back to the defaults.

## A final APK

```
just apk
```

writes `dist/com.graic.gesh-Signed.apk`. Install it on any Quest in
developer mode with `adb install dist/com.graic.gesh-Signed.apk`.

That's signed with the local Android debug key, which is fine for
sideloading. For the store build, signed with your own key, see
`notes/publishing.md` (`just apk-store`).

## Other commands

- `just bench [runs] [faces]`: performance on the headset with a scripted
  scene, see `plans/perf.md`.
- `just sk-pull`: pull the latest StereoKit develop into `./stereokit` (a git
  subtree).
- `python scripts/notices.py`: rebuild `Assets/notices.txt`, the license
  notices shown in Home > Info, from the dependencies on disk. Run it after
  a `just play` whenever a dependency changes.
- `python scripts/icons.py`: re-render the menu icons in `Assets/icons` from
  the Material Symbols SVGs in `icons/` (needs Pillow and Edge; add
  `--download` to fetch the SVGs again).

## Layout

- `Program.cs`: the frame loop: input, hands, menus, drawing, autosave.
- `GEditableMesh.cs`, `GHand.cs`: the mesh and what hands do to it.
- `GPaint.cs`: paint atlas. `GHistory.cs`: undo and redo.
- `GPalmMenu.cs`, `GPaintTab.cs`, `GHome.cs`, `GQuickPalette.cs`, `GHints.cs`:
  the UI, see `plans/ui-design.md`.
- `GFiles*.cs`, `GSaver.cs`: saving and loading `.glb` files.
- `plans/`: design notes for each feature.
- `stereokit/`: the StereoKit fork.
