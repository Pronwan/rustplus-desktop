# EventSoundProbe

Throwaway measurement tool. Not part of `RustPlusDesk.sln` — delete the folder if the idea
does not pan out.

It answers exactly one question: **are Rust's server-wide event sounds reliably recognisable
while the game is actually being played**, with gunfire, music and ambience in the mix? If
the answer is no, the crowd-sourced event pipeline is dead and nothing else needs building.

## Running it

```
dotnet run --project tools/EventSoundProbe -- selftest
dotnet run --project tools/EventSoundProbe -- selftest --noise 0.30
dotnet run --project tools/EventSoundProbe -- listen
dotnet run --project tools/EventSoundProbe -- devices
```

`listen` calibrates, then captures the **system mix** and prints a timestamped line per
detected event. Run it for a full session and compare the log against what really happened
on the server — misses matter as much as false positives.

## How it works

Landmark ("Shazam"-style) fingerprinting, not spectral correlation. Correlation compares
whole frames and collapses once the cue is buried under other audio; landmark hashing keys
only on local spectral peaks and the offsets between them, so unrelated loud sounds add
peaks without removing ours. 16 kHz mono, 1024-sample FFT, 256-sample hop, six logarithmic
bands, peak pairs hashed as `(f1, f2, dt)`. A match is a histogram spike at one time offset.

**Thresholds are calibrated, not guessed.** Raw scores scale with clip length and hash count
— a 16 s clip scores an order of magnitude above a 5 s one for the same match quality, so
one global number is meaningless. `Calibrate` measures the self-score under noise against
the worst cross-group score and puts the threshold between them.

## The two questions, measured separately

Clips that sound alike do **not** mean the same thing:

| clip | means |
|------|-------|
| `monument-event-deep-sea-open` | Deep Sea opened |
| `deepsea-wipe-alarm-loop` | Deep Sea is closing — only audible *inside* the zone |
| `monument-event-cargo-ship-spawn` | Cargo spawned |
| `cargo-ship-horn` | Cargo sounding its horn while already active |

So calibration reports two ratios. `detect` = self vs. worst *other-group* score: can we tell
which event this is at all? `cue` = self vs. worst *same-group* score: can we tell which of
the group's meanings it carries?

| noise | detect | cue |
|-------|--------|-----|
| 0.15 | 4.1×–30×, all groups clean | deep-sea **1.0×**, cargo 1.9× |
| 0.30 | 2.1×–21×, still clean | deep-sea **1.0×**, cargo 1.5×–1.6× |

**Detection works. Cue discrimination does not, and will not.** `deep-sea-open` and
`deep-sea-despawn` are a coin flip — at noise 0.15 the wrong clip actually scores higher
(1092 vs. 1061). The cargo pair is no better. `horn_disant` is the exception: it separates
from its own group at ~80×, so the distant horn really is a different sound.

That is not a blocker, because the cue never has to come from the audio:

- **Deep Sea stays open exactly 3 hours.** Detect the open, derive the close. The despawn
  alarm should not be a trigger at all — it is only audible to someone inside the zone, so
  its coverage is near zero anyway, and keeping it armed only risks reading a close as an
  open.
- **Cargo:** no cargo active ⇒ the cue is the spawn; cargo active ⇒ it is the horn. The one
  failure mode is a client connecting mid-cargo that never saw the spawn — shared server
  state covers it as soon as anyone else reported the spawn.

In other words: **the fingerprint answers "which group", the event state machine answers
"which cue".** `Fingerprint.GroupOf` and `Fingerprint.CueOf` keep those apart on purpose.

`excavator` and `oil-rig` are alone in their groups, so they have no cue problem at all —
they are just the weakest on raw detection (2.1× and 3.2× at noise 0.30). A second recording
of each would give them the same redundancy the cargo group gets from its two horns.

**These numbers are not the answer.** White noise covers the whole spectrum and is harsher
than gunfire in that respect, but it lacks the structured, transient content of real game
audio that can produce spurious peaks. Only a real session settles it.
