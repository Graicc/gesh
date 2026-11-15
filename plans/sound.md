# Plan: sound effects

Status: implemented in `GSounds`. The brush uses the "muted felt marker"
for now; a better brush sound is still to find. The menu already gets
StereoKit's UI sounds (button press/release); this covers everything else.

## Principles

- **Confirm what the hands did**, since a hand-tracked pinch has no physical
  click. Every pinch that does something gets a sound; a pinch that does
  nothing (missed, air) stays quiet or gets the world-move sound, so the ear
  tells you which happened.
- **Spatial**: played at the hand or the part, so two hands sound like two
  hands.
- **Short and soft**: under 150 ms, quiet, no music. Varied pitch (±5%
  random) so repeats don't grate.
- **Menu buttons use StereoKit's UI sounds** (press and release), which they
  already play: undo, redo, Home and the second tap of a two-tap button
  get nothing of their own.
- **Generated, not recorded**: `Sound.Generate` from a few lines of code each,
  like the icons, so there are no asset files and they're easy to tune.

## The sounds

| Action | Sound | Where |
|---|---|---|
| grab a part (pinch on it) | StereoKit's window grab sound | part |
| release | StereoKit's window let-go sound | part |
| merge on release | snap: click + short rising blip | merged vertex |
| extend (pull out) | soft "pop" | new vertex |
| a pulled face forms | the extend pop | face centroid |
| delete | the extend pop | part |
| brush stroke | continuous, louder and brighter with fingertip speed, fades when not touching; plain band-passed noise sounded like static, options on the sound board | fingertip, follows it (`SoundInst.Position`) |
| paint bucket fills a face | the extend pop | face |
| pipette picks a color | the extend pop | fingertip |
| quick palette appears / hover changes / ends | StereoKit's window grab / button click / window let-go | hand |
| air pinch starts / stops moving the world | StereoKit's window grab / let-go | hand |
| a scale between the hands (zoom, or a two-hand grab) passes each 5% step (×1.05) | StereoKit's button click | between the hands |
| palm menu appears / goes away | glass chime: D4 (294 Hz) then A4 (440 Hz), 110 ms apart (down: A4 then D4), each a slightly detuned pair of sines (×1.004), soft attack, 340 ms tails, 2-pole low-pass at 704 Hz | menu |
| Home opens / closes | button press: StereoKit's UI sounds | the button |
| save done | two-note chime up | Home |
| an action needs a second tap (armed) | button press: StereoKit's UI sounds | the button |
| something failed (save, open) | two-note down | Home |
| undo / redo | button press: StereoKit's UI sounds | the button |

The sound board (an artifact made from this plan) plays a first pass at every
one of these.

## Structure

- `GSounds` (new): makes each `Sound` once at startup with `Sound.Generate`,
  and `Play(GSoundId id, Vec3 at)` adds the pitch jitter. The brush swish is
  one `SoundInst` per hand, kept and updated.
- Events, not calls from deep code: `GHand.Tick` returns what happened this
  tick (`GHandEvents`: grabbed part, released, merged at, extended at, deleted
  at, filled face at, picked), the same way it now exposes `Picked`. Program
  hands them to `GSounds`. The mesh and hand logic stay silent and testable;
  the fuzzer can assert events too (e.g. a delete event iff the part count
  dropped).
- Menu/Home sounds are played where those already know (`GPalmMenu.Tick` on
  show/hide transitions, `GHome` on save results).
- Settings: one "Sounds" volume slider in Home > Settings (0 is off). It sets
  the Sfx and Ui buses, so StereoKit's menu sounds follow it too.

## Order

1. `GSounds` with grab, release, merge, extend, delete (the edit loop).
2. Paint: bucket plop, pipette blip, then the brush swish (the one that needs
   tuning on the headset).
3. Menu, Home, palette, zoom detent, undo/redo.
