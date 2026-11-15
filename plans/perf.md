# Plan: performance hill climbing on the headset

How to make gesh faster by measuring on the connected Quest, trying one
change at a time, and keeping only what measurably helps. Aim for big wins
(whole milliseconds per frame), not micro-tuning.

## Setup

1. Headset connected over USB, `adb devices` lists it.
2. Keep it rendering while nobody wears it. Otherwise the runtime pauses the
   app when the proximity sensor says "not worn" and every number is junk:
   ```
   adb shell am broadcast -a com.oculus.vrpowermanager.prox_close
   ```
   (`prox_open` undoes it. It resets on reboot.) Launches are also blocked
   while the boundary (Guardian) dialog shows, which it does with nobody
   wearing the headset: `adb shell setprop debug.oculus.guardian_pause 1`,
   and back to `0` afterwards. `just bench` does all of this itself.
3. Lock clocks so runs are comparable, since the Quest scales them with load:
   ```
   adb shell setprop debug.oculus.cpuLevel 2
   adb shell setprop debug.oculus.gpuLevel 2
   ```
   Measure everything at the same levels. Clear them with `-1` afterwards.

## The benchmark

Hand tracking doesn't run with nobody wearing the headset, so measuring needs
a scripted scene that doesn't depend on input: `just bench` deploys the app
with a `bench` flag file, which makes it:

- Build a deterministic heavy mesh (repeated face extends, about 1000
  faces, 1500 edges), painted if painting exists.
- Slowly rotate the world, so culling and edges change every frame.
- Simulate a held vertex moving every frame, so the per-frame rebuilds run.
- After 10 seconds of warmup, record 30 seconds of frames and log one line:
  `BENCH cpu_ms p50/p95, frames`, where cpu_ms is the managed time of the
  frame callback (Stopwatch).

The GPU side comes from the runtime, not the app:

```
adb logcat -s VrApi
```

prints a line every second like
`FPS=72/72,...,CPU4/GPU=2/2,...,App=4.31ms,GD=0.00ms,CPU&GPU=7.10ms,...`.
`App` is GPU time per frame, `CPU&GPU` the total; `Stale` counts dropped frames.
`just bench` collects these for the measured 30 seconds and prints the median.

## The loop

1. Measure the baseline 3 times. The spread between runs is the noise floor.
2. Pick the change with the biggest expected win (list below, or whatever the
   numbers point at: if GPU `App` time is near the frame budget, work on GPU
   cost; if `cpu_ms` is, work on CPU).
3. Change one thing, deploy, measure 3 times.
4. Keep it only if the median improves by more than the noise floor and
   nothing looks different. Otherwise revert it.
5. Record every attempt, kept or not, in `plans/perf-log.md`: what changed,
   before/after numbers, kept or reverted.
6. Run the fuzzer after any change to mesh code.
7. Repeat until nothing on the list gives a measurable win.

Frame budget: 72 Hz is 13.9 ms, 90 Hz is 11.1 ms, 120 Hz is 8.3 ms. Once well
under the 90 Hz budget, try raising the refresh rate.

## Candidates, roughly by expected size

GPU:
- The floor: a 30 m transparent cube with `fwidth` grid math covers much of
  the view, all blended. Shrink it to the fade radius, make it a single quad,
  or cut it off at the fade distance with `discard` earlier.
- The see-through backface pass is blended over the whole mesh. Check its cost.
- MSAA level and render resolution (`SK settings` / XR swapchain scale).
- Foveated rendering, if StereoKit exposes it on Quest.

CPU:
- Rebuilding and uploading the face and edge meshes every frame even when
  nothing moved (a change counter on the mesh would skip it; check with the
  user before adding the state, since they preferred reusing memory over a
  dirty flag).
- LINQ enumerators (`Indices()`, `Items()`) in per-frame loops allocate and
  are slow on Mono/Android. A plain `for` over the alive flags is cheaper.
- `GetClosestPart` runs every pinch and scans everything. Probably fine.
- One `Mesh.Sphere.Draw` per vertex. StereoKit instances these, so probably
  fine, check.
- Paint: the brush `RenderTo` per frame while painting, and the atlas size.
