# RIT / XIT plumbing — design

**Status:** DRAFT — pending operator smoke test, then maintainer review.
**Date:** 2026-05-16.
**Author:** Simone Fabris (iu3qez), with Claude.
**Upstream issue:** none yet — pairs with #300 (CW-only missing features) by
enabling the RIT-aware Zero Beat path that issue #300's design doc deferred.
**Branch (forthcoming):** `iu3qez/rit-xit` off `upstream/develop`.
**Pairs with:** `docs/designs/cw-zero-beat.md` (Zero Beat).

## TL;DR

Zeus today has a placeholder `RIT` button in the transport bar
(`App.tsx:773`) that does nothing, and TCI stubs (`TciSession.cs:1095-1134`)
that ignore RIT and XIT set-commands. This PR turns both into a real
feature: a single cycle button drives a 3-state machine
(OFF → RIT → XIT → OFF), an offset sub-row appears under the VFO display
with ▲/▼ spinners and a Clear button, and the existing host-side
frequency-offset pipeline (CwOffset, freq-correction #325) grows a second
branch so RX and TX wire frequencies can diverge.

**Post-CTUN update (2026-05-22).** Since the first draft, upstream landed
CTUN (Click-Tune, issue #427, commit 893b94e). The wire model now carries
three frequencies — `VfoHz` (dial), `RadioLoHz` (frozen hardware NCO when
CTUN is on), plus the CW pitch via `EffectiveLoHz`. RIT integrates with
CTUN's WDSP shift mechanism without breaking the rename; XIT in CTUN-on
needs a separate UX call. See §"Interaction with CTUN" for the full
revised wire formula and the open call on XIT × CTUN.

This PR is the **substrate** that makes RIT-aware Zero Beat possible — a
follow-up PR will extend `RadioService.ZeroBeat` to target the RIT register
when RIT is active. The two PRs are independent: this one stands on its own
as a feature ham operators actually want.

## Goal

Match the standard ham-radio RIT/XIT workflow: operator engages RIT, dials a
small offset (±9.999 kHz max), the RX is shifted while TX stays on the dial.
XIT mirrors that for TX. One offset active at a time, single cycle button to
switch between RIT and XIT and back to OFF, per-mode offset preserved across
the cycle so the operator can park RIT, try XIT, and come back to find their
RIT value still there.

Operating model and constants come from Thetis (the canonical reference for
Zeus), with one deliberate divergence: Zeus enforces RIT and XIT to be
mutually exclusive (one mode active at a time). Thetis allows both flags on
simultaneously because RIT applies during RX and XIT during TX, so they don't
fight each other. Zeus prefers the simpler UI — one cycle button, one offset
visible at a time. Documented in §"Open questions for the maintainer" below
because it's worth Doug's call.

## Tech stack

.NET 10 / C# 12 backend, React 19 + Vite frontend, xUnit 2.9 (backend tests),
Vitest 2.1 (frontend tests). No new runtime dependencies; the work fits in
the existing project layout.

## Background

### Why this is not just a UI wiring change

You'd think wiring up the placeholder `RIT` button is a one-day job — just
add an event handler, send the offset to the radio, done. It isn't, because
of how Zeus currently models radio frequency.

Today the wire format treats RX freq and TX freq as the same number. In
`ControlFrame.cs:200-205` the switch case for the five frequency registers
(`TxFreq`, `RxFreq`, `RxFreq2`, `RxFreq3`, `RxFreq4`) writes `state.VfoAHz`
into all of them. The comment explicitly notes this:

> *"All five frequency registers carry the same VfoAHz here — Zeus has no
> separate TX VFO. ... for CW, EffectiveLoHz is already baked into VfoAHz
> upstream in RadioService.SetVfo."*

RIT means RX freq diverges from the dial. XIT means TX freq diverges. Both
together (which Thetis allows but Zeus won't) means RX and TX diverge in
different directions. Either way, the single-number model has to grow into a
two-number model. This is a wire-layer change with a small but real blast
radius across `Zeus.Contracts`, `Zeus.Protocol1`, `Zeus.Protocol2`,
`RadioService`, and the freq-correction PR #334.

### Why RIT separately from SPLIT

RIT and SPLIT serve overlapping use cases — both let the operator listen on
one frequency and transmit on another — but they're mechanically different,
and conflating them causes confusion in the codebase and the UI.

- **RIT/XIT** = a small signed offset (typically ±9.999 kHz on commercial
  rigs) added to one register. Use case: a caller is slightly off-frequency,
  drift, fine alignment in pile-ups.
- **SPLIT** = two independent VFOs (A and B), arbitrarily far apart. Use
  case: a DX station on 14.220 listening up 5–10 kHz at 14.225–14.230.

Most commercial rigs implement both, and they compose (you can RIT on top
of SPLIT). Zeus has a `SPLIT` placeholder in `App.tsx:772` next to the `RIT`
one; that's a separate feature deserving its own PR.

### Why not just inherit from frequency-correction #325 / #334

Doug's frequency-correction PR uses host-side injection at the
`Protocol1Client.SetVfoAHz` / `Protocol2Client.SetVfoAHz` seam: the operator
sets a calibration factor, and the client multiplies before writing to the
wire. We could mirror that pattern for RIT/XIT — keep `SetVfoAHz`'s single
parameter, add `client.SetRitOffset(hz)` / `SetXitOffset(hz)` setters,
combine inside the client.

We didn't. Two reasons:

1. The freq-correction factor is conceptually one global multiplier applied
   to every freq the client sees. RIT/XIT are different: they're additive,
   they vary by mode (off / rit / xit), and the *math is different for RX
   vs TX*. Treating them as "yet another client-side adjustment" buries
   the rx-vs-tx split inside two protocol clients instead of in
   `RadioService` where the operator state lives.

2. The `ControlFrame.cs` comment is an invitation: *"Zeus has no separate
   TX VFO yet"*. The codebase is ready for the rename; the comment was
   left by whoever drew the line and stopped just short.

The Thetis reference (`console.cs:31773-31787`, see receipts) calculates
`rx_freq` and `tx_freq` independently in the orchestrator and pushes both
to the wire. We follow that pattern.

## Design

### Server-side state model

`RadioService` grows three new fields (runtime only, never persisted to
LiteDB):

```csharp
private IncrementalTuningMode _itMode = IncrementalTuningMode.Off;
private int _itRitHz;   // ±9999, preserved across cycle transitions
private int _itXitHz;   // ±9999, preserved across cycle transitions

public enum IncrementalTuningMode : byte { Off = 0, Rit = 1, Xit = 2 }
```

The `enum` makes mutual exclusion structural — you cannot represent
`RitOn && XitOn`. Three states, three states only.

### Effective wire-frequency formula

The single load-bearing piece of math:

```
rxWireHz = dial + cwOffset(mode) + ritDelta
txWireHz = dial + cwOffset(mode) + xitDelta

ritDelta = (_itMode == Rit) ? _itRitHz : 0
xitDelta = (_itMode == Xit) ? _itXitHz : 0
cwOffset(CWU)   = −cwPitchHz   (≈ −600)
cwOffset(CWL)   = +cwPitchHz   (≈ +600)
cwOffset(other) = 0
```

Worked example: dial 14.050.000, CWU mode (pitch 600 Hz), RIT engaged at
+250 Hz →
- `rxWireHz = 14_050_000 − 600 + 250 = 14_049_650`
- `txWireHz = 14_050_000 − 600 + 0   = 14_049_400` (XIT off, dial freq for TX)

The CwOffset baking is inherited from existing CW handling (`CwOffset.cs`).
RIT/XIT layer on top.

### Wire-layer changes — the rename

The single biggest invasive change in this PR. We rename one symbol along
the entire path it touches:

- `Zeus.Protocol1.ControlFrame.CcState.VfoAHz` →
  splits into `RxFreqAHz` and `TxFreqAHz` (both `long`, signed for now to
  match the existing field; clamped to `uint` range at the wire boundary).
- `Zeus.Protocol1.Protocol1Client.SetVfoAHz(long)` →
  becomes `SetVfos(long rxHz, long txHz)`. Underlying private fields
  `_vfoAHz` becomes `_rxFreqHz` + `_txFreqHz`.
- Same in `Zeus.Protocol2.Protocol2Client` (which already has `_rxFreqHz`
  — adds `_txFreqHz` alongside).
- `ControlFrame.WriteCcBytes` switch case at line 195 routes:
  ```csharp
  case CcRegister.RxFreq:
  case CcRegister.RxFreq2:
  case CcRegister.RxFreq3:
  case CcRegister.RxFreq4:
      writeBE32(state.RxFreqAHz);
      break;
  case CcRegister.TxFreq:
      writeBE32(state.TxFreqAHz);
      break;
  ```
- The frequency-correction factor (#325) applies separately to each field
  inside the client's setter, mirroring the existing single-field logic.

Backward compatibility: when nobody calls `SetRit*` / `SetXit*`, the
orchestrator pushes `rxWireHz == txWireHz == dial + cwOffset(mode)`. Same
bytes on the wire as before. No behavioral change for any radio that
doesn't see RIT/XIT.

### REST endpoint

One endpoint, replace-semantics:

```http
POST /api/rx/incremental-tuning
Content-Type: application/json

{ "mode": "off" | "rit" | "xit",
  "offsetHz": -9999..9999 }
```

Whole-state set, not PATCH. The operator's edit (cycle a mode, dial an
offset) is always a "this is the new state" decision, not a partial diff.
Response body: the post-clamp state (200 OK), so the frontend can pick up
the clamped value if the input was out of range.

400 Bad Request for malformed payloads (mode not in the enum, offsetHz not
an integer). Out-of-range `offsetHz` is silently clamped — operationally
nicer than rejecting, and matches Thetis's `Math.Min`/`Math.Max` pattern.

`GET /api/state` (existing) returns the runtime snapshot, extended with
`itMode`, `ritOffsetHz`, `xitOffsetHz`. Frontend already broadcasts state
via SignalR `StreamingHub` — the new fields ride along.

### Frontend UI surface

Two new components, one existing component touched:

**`IncrementalTuningButton.tsx`** (new — replaces `App.tsx:773` placeholder)
- Renders as a single button in the transport bar.
- Single click cycles: OFF → RIT → XIT → OFF.
- Label changes per state: dim "RIT/XIT" when OFF, lit "RIT" or "XIT" with
  `--accent` border when active.
- Position: same slot the current `<button>RIT</button>` placeholder
  occupies. No new transport-bar real estate.

**`RitXitOffsetRow.tsx`** (new — sub-row under VfoDisplay)
- Renders only when `mode != Off`.
- Layout (monospace, faithful-to-HL2-aesthetic):
  ```
  [ RIT  ▼  +0250 Hz  ▲  Clr ]
  ```
- ▲ / ▼ spinners: increment / decrement by **filter-aware step** (10 Hz
  default, 5 Hz when current filter bandwidth ≤ 250 Hz — matches Thetis at
  console.cs:7624-7633).
- Numeric display is click-to-edit: click → text input, Enter commits.
- `Clr` button: zeros the active mode's offset AND exits the mode
  (`_itMode = Off`). The inactive mode's offset is preserved. This is the
  operator's "I'm done with this" gesture. Single click, no chord.
- Debounced POSTs: 100 ms after the last ▲/▼ click so a held button doesn't
  blast the endpoint.

**`VfoDisplay.tsx`** (modify)
- Renders `<RitXitOffsetRow />` immediately below the main VFO numbers when
  `ritEnabled || xitEnabled`. Otherwise, nothing — no permanent real
  estate stolen.

### Auto-clear events

| Event | `_itMode` | `_itRitHz` | `_itXitHz` |
|---|---|---|---|
| Cycle button (any transition) | changes | preserved | preserved |
| `Clr` button (mode = Rit) | Off | **0** | preserved |
| `Clr` button (mode = Xit) | Off | preserved | **0** |
| Band change | Off | **0** | **0** |
| Mode change (CWU/CWL/USB/...) | Off | **0** | **0** |
| Radio disconnect / reconnect | Off | **0** | **0** |
| (future) SPLIT engaged | Off | **0** | **0** |
| (future) Memory recall | overwritten | overwritten | overwritten |

"Preserved" means the value sits in `RadioService` state but is not applied
to the wire while `_itMode = Off`. Cycle back to the same mode → the value
reappears in the sub-row and lands on the wire.

### TCI handling

The existing TCI stubs at `Tci/TciSession.cs:1095-1134` get real handlers,
mapped to the Thetis CAT command names (canonical reference: Thetis
`CATCommands.cs`):

- `ZZRT` — RIT on/off (set/query)
- `ZZRF` — RIT offset frequency (Hz, signed, 5 digits, e.g. `ZZRF+00250;`)
- `ZZRU` / `ZZRD` — RIT step up/down (uses the filter-aware step)
- `ZZXS` — XIT on/off (note: not `ZZXT`)
- `ZZXF` — XIT offset
- `ZZXU` / `ZZXD` — XIT step up/down

Each command routes into the same `RadioService.SetIncrementalTuning(...)`
the REST endpoint uses, so wire side-effects and state broadcast are
identical regardless of source.

**Mutual-exclusion divergence from Thetis.** If a TCI client (WSJT-X,
N1MM+, etc.) sends `ZZRT1;` while XIT was already on, our backend cleanly
transitions: `_itMode = Rit`, `_itXitHz` is preserved in memory (so cycling
back to XIT restores it), but XIT is no longer applied to the wire. Thetis
would leave both flags on. The operator's `_xitHz` is not lost — only its
application is suspended while `_itMode != Xit`. Worth flagging to Doug;
see §"Open questions for the maintainer".

If Doug prefers, this TCI block can split into a follow-up PR to make this
one atomic on the substrate alone.

## Interaction with CTUN (issue #427)

This section was added in the 2026-05-22 revision after upstream landed
CTUN (commit 893b94e). It revises §"Effective wire-frequency formula"
above without contradicting the rest of the design — the substrate
(`VfoAHz` → `RxFreqAHz` + `TxFreqAHz` rename, `SetVfos(rx, tx)` setter,
the cycle button, the auto-clear table) stays as written. What changes
is the orchestrator math.

### The three-frequency model post-CTUN

`StateDto` now carries three frequency-related fields:

- `VfoHz` — operator dial (what they see)
- `RadioLoHz` — hardware NCO frequency (where the radio is physically
  tuned)
- `Mode` — RxMode driving `EffectiveLoHz(VfoHz, mode)` (the existing
  helper that bakes in the CW pitch shift)

CTUN OFF: `RadioLoHz == VfoHz` always. Effective hardware frequency
is `EffectiveLoHz(VfoHz, mode)`.

CTUN ON: `RadioLoHz` is frozen at whatever value `EffectiveLoHz(VfoHz,
mode)` had when CTUN was toggled. `VfoHz` roams freely. WDSP's `shift`
stage (RXA-side only, see `Zeus.Dsp/Wdsp/WdspDspEngine.cs:528-537`) is
fed `shiftHz = EffectiveLoHz(VfoHz, mode) - RadioLoHz` so the dial
position still demodulates to audio baseband. Code paths:
`RadioService.SetVfo` skips the wire retune when CTUN is on
(`RadioService.cs:582-598`); `DspPipelineService.OnRadioStateChanged`
pushes `RadioLoHz` to the wire instead of `EffectiveLoHz(...)` and
calls `engine.SetCtunShift(channel, shiftHz)` with the dial offset
(`DspPipelineService.cs:540-555`).

### RIT in CTUN — rides the shift

RIT shifts the RX-effective frequency by `ritDelta` Hz. Both CTUN modes
have a clean integration:

**CTUN OFF (legacy path, what the v1 design described):**

```
rxWireHz = EffectiveLoHz(VfoHz + ritDelta, mode)
shiftHz  = 0
```

The radio NCO retunes; no WDSP shift.

**CTUN ON (new path):**

```
rxWireHz = RadioLoHz                                       (unchanged — radio frozen)
shiftHz  = EffectiveLoHz(VfoHz + ritDelta, mode) - RadioLoHz
```

The radio NCO stays at `RadioLoHz`. WDSP's shift gets extended by
`ritDelta` so the demod hears the dial-plus-RIT position. The operator
gets "no spectrum reflow" UX for free — same property CTUN already
provides.

Implementation-wise, `DspPipelineService.OnRadioStateChanged` already
computes `ctunShiftHz` (line 548). It only needs to read `_itRitHz` from
the snapshot when `_itMode == Rit` and add it to the shift:

```csharp
int ritDelta = (s.ItMode == IncrementalTuningMode.Rit) ? s.RitOffsetHz : 0;
int ctunShiftHz = s.CtunEnabled
    ? (int)(CwOffset.EffectiveLoHz(s.Mode, s.VfoHz + ritDelta) - s.RadioLoHz)
    : 0;
```

And the CTUN-OFF wire push needs the same delta:

```csharp
long rxWireHz = s.CtunEnabled
    ? s.RadioLoHz
    : CwOffset.EffectiveLoHz(s.Mode, s.VfoHz + ritDelta);
```

### XIT in CTUN — three options, recommend (A)

XIT shifts TX. WDSP's `shift` stage is RX-only — `SetRXAShiftFreq` and
`RXANBPSetShiftFrequency` both touch the analyzer chain. There's no
symmetric TX-side shift in WDSP. So XIT can't simply ride the CTUN
shift the way RIT does.

Compounding: today Zeus already pushes `RadioLoHz` to the wire during
MOX when CTUN is on (`DspPipelineService.cs:513`). That means TX in
CTUN-on transmits at the frozen NCO position, **not** at where the
operator's dial sits. Whether this is intentional or a known limitation
of the CTUN PR is itself worth checking — see §Open questions.

Three candidate approaches for XIT × CTUN:

| | Approach | Implication |
|---|---|---|
| **A** | XIT silently disabled while CTUN is on | Cycle button OFF→RIT works; OFF→RIT→XIT skips XIT and goes OFF→RIT→OFF when `CtunEnabled`. If operator wants XIT, they disable CTUN first. Cleanest semantic, smallest implementation, lines up with CTUN's existing TX-in-CTUN limitation. |
| **B** | XIT auto-disables CTUN on MOX, re-enables on MOX release | The radio retunes to `EffectiveLoHz(VfoHz + xitDelta, mode)` for TX, snaps back to `RadioLoHz` on key-up. Operator intent honoured. Cost: MOX-edge plumbing through `RadioService` and the wire layer, two persistent state transitions per keying event. |
| **C** | Full CTUN-aware TX retuning without disabling CTUN | Wire TxFreqAHz gets `EffectiveLoHz(VfoHz + xitDelta, mode)` while wire RxFreqAHz stays at `RadioLoHz`. The radio NCO has to follow the TxFreq slot during MOX (Protocol-1 frame already routes TxFreq separately, see `Protocol1Client.cs:870-883`). Most powerful, biggest implementation, breaks CTUN's "frozen NCO" invariant during keying. |

**Recommendation: A.** Document the limitation, give operators a clean
state machine, defer (B) or (C) to a future PR if real-world use surfaces
the gap. Two reasons:

- CTUN is mostly RX-time band-scanning. XIT use cases (chasing a drifting
  station you're in QSO with) tend to assume "I'm in a stable conversation,
  not band-scanning". The two workflows rarely overlap.
- Doug's CTUN PR already accepts the limitation that TX in CTUN-on goes
  to `RadioLoHz`. Adding XIT × CTUN complexity on top is a separate
  conversation; A defers it cleanly.

### Wire-formula consolidated

The complete post-CTUN orchestrator formula:

```
ritDelta = (ItMode == Rit) ? RitOffsetHz : 0
xitDelta = (ItMode == Xit && !CtunEnabled) ? XitOffsetHz : 0       // option A

rxWireHz = CtunEnabled ? RadioLoHz
                       : EffectiveLoHz(VfoHz + ritDelta, Mode)
txWireHz = CtunEnabled ? RadioLoHz
                       : EffectiveLoHz(VfoHz + xitDelta, Mode)
shiftHz  = CtunEnabled ? EffectiveLoHz(VfoHz + ritDelta, Mode) - RadioLoHz
                       : 0
```

When `ItMode == Off`, both deltas are zero and the formula collapses to
the current (pre-RIT/XIT) CTUN behaviour byte-for-byte. When CTUN is OFF
and ItMode != Off, it matches the v1 design exactly. The new behaviour
only kicks in when both are active simultaneously, which is RIT × CTUN.

### Persistence asymmetry

CTUN persists to LiteDB across reconnect (commit 893b94e adds
`CtunEnabled` and `RadioLoHz` to `RadioStateStore`). RIT/XIT do **not**
persist (the original design's auto-clear table treats disconnect as a
true clear).

On reconnect with `CtunEnabled = true` persisted:
- The radio retunes to the persisted `RadioLoHz`
- `_itMode = Off`, `_itRitHz = 0`, `_itXitHz = 0`
- Operator must re-enable RIT manually if they want it

This asymmetry is intentional: CTUN is a mode-config (operator's
preferred way of operating), RIT/XIT are momentary state (where you are
in the current QSO). It matches Thetis behaviour where RIT clears on
power cycle but CTUN-equivalent does not.

### Frontend coexistence

Post-CTUN the transport bar reads `SPLIT | RIT | SAVE MEM | CTUN`. CTUN
is the new button at the right end of the row (commit 893b94e adds it
after SAVE MEM). The `RIT` placeholder at `App.tsx:773` is still vacant
— our cycle button slots in there with no further layout work.

Visual coexistence: both buttons use the same `btn ghost hide-mobile`
class and `--accent` lit-when-active treatment. Two adjacent buttons
both potentially lit (CTUN engaged + RIT cycle active) is fine — they
represent orthogonal modes and the operator already deals with the same
visual pattern from MOX / TUN / PS toggles on the left side of the bar.

### Test impact

The §Testing-strategy buckets stay as written, plus three new cases:

1. **Orchestrator test** — `SetIncrementalTuning(Rit, +250)` with `CtunEnabled=true`: assert no wire VfoHz change, assert `SetCtunShift` was called with `EffectiveLoHz(VfoHz + 250, mode) - RadioLoHz`.
2. **Orchestrator test** — `SetIncrementalTuning(Xit, +500)` with `CtunEnabled=true`: under option (A), assert state mutation is rejected or the offset is stored but not applied to wire. Behaviour to confirm with Doug.
3. **Reconnect test** — `CtunEnabled=true` persisted, `_itRitHz=+250` runtime: simulate disconnect/reconnect, assert CTUN restored, RIT cleared.

## Data flow

**Operator clicks the cycle button (OFF → RIT):**

```
IncrementalTuningButton click
  → client.ts setIncrementalTuning({ mode: "rit", offsetHz: lastRitHz })
  → POST /api/rx/incremental-tuning
  → ZeusEndpoints → RadioService.SetIncrementalTuning(...)
       1. validate + clamp via RitXitMath
       2. _itMode := Rit, _itRitHz := offsetHz
       3. compute rxWireHz, txWireHz (formula above)
       4. ActiveClient.SetVfos(rxWireHz, txWireHz)
       5. Mutate snapshot, SignalR broadcast
  → Protocol1Client.SetVfos
       6. apply freq-correction to each field
       7. update _rxFreqHz, _txFreqHz
       8. rotation 4-phase carries new values on next tx packet (~3 ms)
  → Frontend store updates, RitXitOffsetRow appears
```

**Operator clicks ▲ on sub-row:**

Same path from step 1, debounced 100 ms. Offset becomes
`current + filterAwareStep(currentFilterBw)`, clamped, sent.

**Operator clicks `Clr` on sub-row:**

```
POST { mode: "off", offsetHz: 0 }  →  ... → 
  _itMode := Off
  if previousMode == Rit: _itRitHz := 0
  else if previousMode == Xit: _itXitHz := 0
  // the other mode's offset is left untouched
  rxWireHz = txWireHz = dial + cwOffset(mode)
  push to wire, broadcast
  → sub-row disappears
```

**Auto-clear on band change:**

```
RadioService.SetBand(newBand)  →  if newBand != _band:
  _itMode := Off, _itRitHz := 0, _itXitHz := 0
  ... (recompute + push, exactly as a manual clear)
```

Same shape in `SetMode` and on disconnect.

## Error handling

### Input validation

| Input | Behavior |
|---|---|
| `offsetHz` out of range (±50_000 etc.) | Silent clamp to ±9999. Response body carries the clamped value so frontend sees the truth. Info log: `api.rx.it.clamp from={raw} to={clamped}` |
| `offsetHz` not an integer / NaN | 400 Bad Request, no state change |
| `mode` not in `{off, rit, xit}` | 400 Bad Request |
| TCI `ZZRF+99999;` (would clamp to 9999) | Accept, clamp, store. Subsequent `ZZRF;` query returns the clamped form |
| TCI command malformed | Ignored (matches Zeus's existing stub-permissive pattern) |

Pure helper `Zeus.Server.Hosting.RitXitMath.ClampOffset(int hz)` centralises
this; tested against
`[-100000, -9999, 0, 9999, 100000] → [-9999, -9999, 0, 9999, 9999]`.

### Wire-layer failures

- **`_activeClient == null`** (disconnected): state is mutated, snapshot
  broadcast, but no wire write. On reconnect the disconnect-clear rule
  fires, so the rehydrated state is clean — no stale offset reaches the
  fresh client.
- **UDP write fails**: fire-and-forget, no retry (matches every other Zeus
  wire write). The rotation schedule re-sends the register within ~3 ms
  regardless.
- **Race between `SetIncrementalTuning` and MOX edge**: `RadioService`
  serialises mutations through the existing `Mutate(...)` lock; the MOX
  edge re-reads the snapshot after each mutation completes, so no torn
  state can reach the rotation.

### Band-edge clamping

Thetis clamps the post-offset wire frequency to band limits *after* adding
the offset (`console.cs:31781`, `31788`). Example: dial 14.349.000 (top of
20 m) + RIT +500 → `rxWireHz` would be 14.349.500, out of band; Thetis
forces it to 14.350.000 (band edge), silently. The displayed offset stays
at +500 but the effective offset is +1000.

We follow Thetis exactly. This means rare drift between displayed offset
and effective offset right at band edges. Worth flagging to Doug because
the alternative (reject the set, or allow out-of-band) is also defensible.

### TCI forced-mode transition

Already covered in §"TCI handling". Recapping: an external TCI client can
switch `_itMode` without operator input. The operator's offset *data* in
the inactive mode is preserved in memory. Only application changes.

Mitigation: an info log entry on the mode-switch (`tci.it.mode-transition
from={x} to={y} source=tci`). No user-facing toast — TCI is machine-to-
machine and the operator shouldn't have to dismiss notifications from it.
Frontend learns about the switch through the normal state broadcast.

## Testing strategy

Six buckets — five automated, one manual, plus a light on-air pass:

### Pure unit tests (`Zeus.Server.Tests/RitXitMathTests.cs`)

Pattern lifted from `ZeroBeatAlgorithmTests`: static helpers, zero deps,
trivially testable. Targets `ClampOffset(int)`, `FilterAwareStepHz(int)`,
and any sign-handling math the orchestrator factors out. ~6–8 tests, runs
in < 100 ms.

### Orchestrator tests (`Zeus.Server.Tests/RadioServiceRitXitTests.cs`)

With a fake `IDspEngine` and a `Protocol1Client` stub that captures pushed
freqs. Cases:

1. `SetIncrementalTuning(Rit, +250)` with dial 14_050_000, CWU, pitch 600
   → pushed `rxWireHz=14_049_650, txWireHz=14_049_400`.
2. Cycle Off → Rit (+250) → Xit (preserve rit) → Off (preserve both).
   Final offsets equal initial dialled values, mode is Off.
3. Band change with active mode → true clear.
4. Mode change → true clear.
5. Reconnect after a disconnect → true clear.
6. Race: 100 concurrent `SetIncrementalTuning` from N threads → terminal
   state matches the final caller's args, no torn `_itMode`.
7. MOX edge with `_itMode = Xit` → `_txFreqHz = dial + xit`, `_rxFreqHz =
   dial`. The two fields differ on the wire as expected.

### Wire-layer tests (`Zeus.Protocol1.Tests/ControlFrameRitXitTests.cs`)

Direct assertions on `WriteCcBytes` output bytes:

1. `state.RxFreqAHz = X, state.TxFreqAHz = Y, register = RxFreq` → BE32
   payload = `X`.
2. Same state, `register = TxFreq` → payload = `Y`.
3. `RxFreq2 / RxFreq3 / RxFreq4` → always `RxFreqAHz`, never `TxFreqAHz`.
4. `state.Mox = true` → cc[0] bit 0 = 1, freq payload unchanged.

### REST endpoint integration (`Zeus.Server.Tests/RitXitEndpointTests.cs`)

`WebApplicationFactory<Program>`-based, hits the endpoint as HTTP.

1. Valid POST → 200 + echoed state in body.
2. Out-of-range `offsetHz` → 200 + clamped value in body.
3. Bad `mode` enum → 400.
4. `GET /api/state` reflects the new fields.

### TCI handler tests (`Zeus.Server.Tests/TciRitXitTests.cs`)

Test the new handlers in `TciSession`:

1. `ZZRT1;` enables RIT mode in `RadioService`.
2. `ZZRF+00250;` sets RIT offset to 250.
3. `ZZRU;` steps up by the current filter-aware step.
4. Mutex divergence: `ZZRT1;` then `ZZXS1;` → terminal `_itMode = Xit`,
   `ZZRT;` query returns `ZZRT0;`. (Documents the divergence in test
   form.)
5. Query forms (no argument) reply with current state in Thetis-compatible
   formatting.

### Frontend (Vitest)

- `IncrementalTuningButton.test.tsx`: state cycle, label changes, accent
  border on active.
- `RitXitOffsetRow.test.tsx`: render gating (only when `mode != Off`),
  spinner clicks issue POSTs with correct payload, `Clr` POSTs `mode:
  "off"`, click-to-edit numeric input commits on Enter.

### Manual smoke test

Pre-merge gate. With the dev stack up and a synthetic engine connected:

1. Cycle button visible in transport bar. Click → label "RIT", accent
   border lit. Sub-row appears under VFO with "RIT 0 Hz".
2. Click ▲ three times → "RIT +30" (or +15 if a narrow filter is active).
3. Click ▼ once → "RIT +20" / "+10".
4. Click `Clr` → sub-row disappears, button label back to "RIT/XIT" dim.
5. Cycle to RIT again → "RIT 0" (last Clr wiped this mode's offset).
6. Cycle to XIT → "XIT 0", RIT preserved in `/api/state` if you check.
7. Change band → sub-row gone, `/api/state` shows offsets 0.
8. Disconnect / reconnect → same.

### On-air validation

This is a **lighter envelope** than the Zero Beat merge gate. Zero Beat is
DSP — three baked-in constants whose correctness only shows on-air. RIT/XIT
is wire plumbing. The algorithm is `dial + offset`; the constants are
clamped at known bounds. What on-air checks is whether the operator's
hands-and-ears reality matches the design intent.

Minimum on-air pass (with a real HL2 or ANAN class radio):

1. Real CW signal slightly off the dial — engage RIT, walk it to bring the
   carrier to the correct pitch. Audio follows.
2. Real SSB station off-frequency — engage RIT, align by ear.
3. XIT in a QSO with a friend — dial-shift TX by some hundreds of Hz,
   confirm they receive on the original dial frequency while you continue
   to hear them on `dial + 0`.
4. Mode change in the middle of an active RIT — sub-row vanishes, dial
   frequency unchanged.
5. Disconnect / reconnect with RIT active — comes back clean.

If any of these surface a UX surprise (the `Clr` button is buried, the
cycle through XIT-to-get-back-to-OFF is awkward, the filter-aware step
feels wrong), flag it; we may need to add the `Esc` global hotkey or `×`
inline reset button after the smoke pass.

## Out of scope / future

These are deliberately not in PR-A. Each gets its own design discussion
when its turn comes.

- **RIT-aware Zero Beat (PR-B).** This PR is the substrate; the follow-up
  small PR teaches `RadioService.ZeroBeat` to target `_itRitHz` (when
  `_itMode == Rit`) instead of the main VFO. The existing `ZeroBeatRequest`
  DTO already has a `byte? RxId` forward-compatible parameter; we add a
  sibling `target: "vfo" | "rit"`.
- **SPLIT.** Independent feature, separate PR. The `SPLIT` placeholder
  in `App.tsx:772` becomes its own button when that work happens.
- **Memory recall.** Not implemented today (the `SAVE MEM` button at
  `App.tsx:774` is also a placeholder). When memory cells arrive, recall
  will overwrite `_itMode` / `_itRitHz` / `_itXitHz` from the stored
  cell. The clear-rule table above already lists this as a hook.
- **Configurable keyboard shortcuts.** Zeus has no hotkey-preferences UI
  today. Hardcoded defaults in this PR (Shift+ArrowUp/Down for step,
  Shift+Backspace for Clear). When a hotkey-prefs panel lands, RIT/XIT
  hotkeys join it.
- **Panadapter dual-marker for RIT/XIT visualisation.** A real UX question:
  with RIT engaged the operator hears `vfo + rit` but the panadapter shows
  one frequency. Operators in split / pile-up situations expect visual
  feedback for the two frequencies (RX and TX). Markers, labels, distinct
  highlights — this is Brian's territory (UI owner, `--accent` token
  discipline). Out of scope here; this PR exposes the data
  (`effectiveRxWireHz` / `effectiveTxWireHz` in the state snapshot) so
  Brian's future PR has everything it needs without protocol changes.
- **`Esc` global hotkey and `×` inline reset.** Provisional alternatives
  to the on-row `Clr` button. Skipped from v1; on-air smoke may surface
  the need.

## Open questions for the maintainer

This section is the "design notes" we'd quote in the PR description to
Doug. Each item is a judgement call we made; happy to revise on his read.

1. **Rename `VfoAHz` → `RxFreqAHz` + `TxFreqAHz` everywhere.** The
   alternative is keeping `VfoAHz` and adding a parallel `TxVfoAHz`. The
   rename matches the wire model (RX and TX really are different fields
   now) and the `ControlFrame.cs:200-205` comment invites it. The cost is
   a wide but mechanical sed across `Zeus.Contracts`, both protocol
   clients, RadioService, and a handful of tests. If you'd rather keep
   `VfoAHz` as the RX-VFO name and add a sibling, say the word.

2. **`SetVfoAHz(long)` → `SetVfos(long rxHz, long txHz)`.** Same shape
   of question. Could stay as `SetVfoAHz` (RX side) + new `SetTxVfoAHz`
   for symmetry, or you might prefer keeping a single setter that takes
   both. We picked the combined setter to make the call sites push both
   atomically — no half-state where RX is updated but TX isn't yet.

3. **REST replace-semantics, not PATCH.** The endpoint takes
   `{ mode, offsetHz }` as one indivisible state. We considered separate
   `/enable` and `/offset` paths, or PATCH with optional fields. The
   single replace endpoint is simpler and the operator action is always
   "this is the new combined state" (you don't typically toggle the mode
   without also implying what offset is in effect). Open to splitting if
   you prefer two narrower endpoints.

4. **Mutual exclusion (Zeus) vs both-on (Thetis).** This is the loudest
   divergence. Thetis allows RIT and XIT to both be on, since they apply
   in opposite MOX phases. We force a single-mode-active model so the UX
   is one cycle button and one sub-row display. The operator's *data*
   isn't lost (both `_itRitHz` and `_itXitHz` persist across cycling),
   only application. If you'd rather we keep Thetis parity here — two
   independent flags, two sub-rows, more UI — we'll redesign.

5. **TCI in this PR vs split-out.** The TCI handlers replace ignore-
   stubs and are ~12 commands (~15-20 lines each). Could ship in this PR
   or as a small follow-up to keep PR-A "substrate only". Your call.

6. **Band-edge clamping Thetis-strict.** When RIT pushes `rxWireHz` past
   the band edge, we clamp silently and let the displayed offset diverge
   from the effective offset (Thetis behavior). Alternatives: reject the
   offset set (cleaner state) or allow out-of-band (some radios cope,
   most don't). We picked Thetis-strict because it never blocks an
   operator action.

7. **Reset UX = on-row `Clr` only.** No `Esc` global hotkey, no `×`
   inline mini-button. Provisional; on-air smoke may say this is too
   buried in a fast QSO. Both alternatives are a few lines of code on
   top of the substrate — happy to add either if the smoke surfaces it.

8. **On-air validation envelope is lighter than Zero Beat's.** Zero Beat
   needed slow/fast/weak/fading because three baked-in constants gate
   real-radio correctness. RIT/XIT is wire plumbing — the math is
   `dial + offset`. The smoke checklist above plus 5 reality-checks
   feels right. If you'd rather we run a fuller envelope, name it.

9. **XIT × CTUN: option (A) — XIT disabled while CTUN is on.** See
   §"Interaction with CTUN". We picked the simplest option: the cycle
   button refuses XIT transitions when `CtunEnabled`, so the operator
   either uses CTUN (RX-side band scanning) or XIT (TX-side offset), not
   both. Alternatives (B) auto-disable CTUN on MOX, or (C) full CTUN-aware
   TX retuning, are both bigger and uglier. If you'd rather we tackle the
   MOX-edge plumbing for (B) or (C), say so and we'll redesign.

10. **CTUN today TX-routes to `RadioLoHz`, not `VfoHz`.** Reading commit
    893b94e, `DspPipelineService.cs:513` pushes `s.CtunEnabled ?
    s.RadioLoHz : EffectiveLoHz(s)` to the wire — meaning during MOX with
    CTUN on, TX happens at the frozen NCO, not the operator's dial
    position. Is that the intended behaviour? If yes, our XIT option (A)
    is internally consistent with it. If it's a known limitation you
    plan to fix, our XIT design might need to follow that fix.

## Notes / receipts

- **CTUN integration** (added 2026-05-22): upstream commit `893b94e`
  (`feat(#427): CTUN — freeze radio NCO while dial roams`) landed
  between the first design draft and now. Touch points to read for
  context: `Zeus.Server.Hosting/RadioService.cs:576-654` (SetVfo gated
  on CTUN, SetCtun toggle), `Zeus.Server.Hosting/DspPipelineService.cs:
  508-555` (P2 wire push + shift computation), `Zeus.Contracts/Dtos.cs:
  259-274` (StateDto fields), `Zeus.Dsp/Wdsp/WdspDspEngine.cs:528-537`
  (the `SetCtunShift` → `SetRXAShiftFreq` plumbing). The shift is
  RXA-only — there is no symmetric TX shift in WDSP, which is the
  technical root of the XIT × CTUN open question.
- **Thetis reference**: `console.cs:31773-31787` for the canonical
  `rx_freq` / `tx_freq` independent calculation, `console.cs:7624-7633`
  for the filter-aware step (10 / 5 Hz), `console.cs:36052` for
  `btnRITReset_Click` (the canonical "clear value + turn off"),
  `console.Designer.cs:2522-2531` for the original ±99.999 kHz range,
  `CATCommands.cs:5904+` for the ZZRF wire format.
- **Range choice**: ±9.999 kHz (commercial-rig style), narrower than
  Thetis's ±99.999. Beyond this range, Zeus will provide SPLIT (separate
  PR). Stated assumption: an operator dialling beyond ±10 kHz really
  wants SPLIT, not RIT.
- **Step choice**: 10 Hz default, 5 Hz when current filter bandwidth ≤
  250 Hz. Lifted verbatim from Thetis. Provisional pending on-air.
- **Cycle-button UX precedent**: not Thetis (which has two independent
  toggles); more like Yaesu's CLAR button on some FT-series radios.
  Documented as a deliberate divergence above.
- **Personal operating experience**: Simo (iu3qez) on CW / SSB DX work.
  Source of the "operator wants the Clr at hand" and "Esc/× as fallback
  if Clr is buried" intuitions.
