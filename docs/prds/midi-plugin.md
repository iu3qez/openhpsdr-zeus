# PRD — MIDI Controller Plugin for Zeus

**Status:** Draft (2026-05-25)
**Author:** Simone IU3QEZ, with AI agent research
**Related:** [`docs/references/thetis-midi2cat-commands.md`](../references/thetis-midi2cat-commands.md) — full Thetis Midi2Cat command inventory and Zeus cross-match
**Supersedes:** [`docs/proposals/midi.md`](../proposals/midi.md) — earlier draft proposal (pre-plugin-system)

---

## 1. Problem Statement

Thetis ships a ~6,000 LOC subsystem called **Midi2Cat** that maps USB MIDI controllers (knobs, faders, buttons, wheels) to radio CAT commands. This is heavily used by operators who run DJ controllers (Behringer CMD PL-1, Numark DJ2GO2 Touch, etc.) or dedicated ham radio MIDI boxes to get tactile, eyes-free control of VFO, drive, filters, and TX state.

Zeus has no MIDI support. Operators migrating from Thetis lose their physical control surface.

### 1.1 Why not port Midi2Cat directly?

Midi2Cat is tightly coupled to Thetis internals:

- **`winmm.dll` P/Invoke** — Windows-only. Zeus targets Windows, macOS, and Linux.
- **Reflection dispatch** — every MIDI event does `MethodInfo.Invoke` keyed by enum name. No compile-time safety.
- **Direct `console.xxx` mutation** — all 211 handler methods reach into the WinForms console object. No abstract command layer.
- **XML DataSet persistence** — fragile schema migration. Zeus uses JSON.
- **No hot-plug** — devices enumerated once at startup.

The command *vocabulary* is the reference; the implementation is not.

## 2. Goals

1. **Thetis command parity on Zeus's existing command surface.** Every Midi2Cat command that Zeus implements as a `RadioService` / `TxService` method (the same surface that TCI clients already use) must be mappable from a MIDI controller on day one. This is **~69 commands** — see §6.
2. **Plugin architecture.** Delivered as a Zeus plugin (`IZeusPlugin` + `IBackendPlugin`), not baked into the core. Installable/removable. Uses the existing plugin system contracts (SDK ABI 1).
3. **Cross-platform.** Windows, macOS, Linux. Input only (no LED output in v1).
4. **Learn mode.** Operator connects a controller, presses "Learn", moves a knob, and assigns it to a Zeus command. No manual MIDI channel/CC editing required.
5. **Persistent mappings.** Saved per-device in the plugin's scoped settings store (`IPluginSettings`). Survive restart.
6. **Hot-plug.** Device connect/disconnect detected at runtime without restart.
7. **Minimal Zeus core change.** One new interface (`IRadioCommandSurface`) extracted from existing `RadioService` / `TxService` public methods and exposed via `IPluginContext`. This is a mechanical refactor — no new logic, no behavior change. `TciSession` and the MIDI plugin both consume the same interface.

## 3. Non-Goals

1. **LED / MIDI output feedback.** The messiest, most device-specific part of Midi2Cat. Deferred to a future version if demand exists.
2. **RX2 commands.** Zeus does not implement RX2. The ~80 RX2/N/A commands from Midi2Cat are out of scope.
3. **Commands Zeus doesn't implement.** Commands where TCI has only stubs (RIT/XIT, split, VFO lock, squelch, CW speed — ack-only, no backend wiring) cannot be wired until Zeus implements the backend. The plugin will grow as Zeus does.
8. **VFO B.** Zeus has no VFO B support. VFO B commands are out of scope.
4. **Thetis `midi2cat.xml` import.** Mappings are user-specific, quick to recreate via learn UI. A converter is not worth maintaining.
5. **MIDI-over-Bluetooth, Network MIDI (rtpMIDI), MIDI 2.0.** Out of scope for v1.
6. **Named preset library.** Single active mapping set per device. Named presets can come later.
7. **Web MIDI API (browser-side).** Excluded — requires HTTPS context, no Safari support, no headless support.

## 4. Architecture

### 4.1 Plugin Package Structure

```
com.openhpsdr.zeus.midi/
  plugin.json
  ZeusMidiPlugin.dll          # .NET 8 assembly
  ui/
    midi-settings.es.js       # React settings panel (ESM)
```

### 4.2 plugin.json

```json
{
  "schemaVersion": 1,
  "id": "com.openhpsdr.zeus.midi",
  "name": "MIDI Controller",
  "version": "1.0.0",
  "author": "OpenHPSDR Zeus contributors",
  "description": "Map USB MIDI controllers to radio commands (VFO, band, mode, filter, TX, DSP)",
  "license": "GPL-2.0-or-later",
  "sdk": { "abi": 1, "minVersion": "1.0.0" },
  "entrypoint": { "assembly": "ZeusMidiPlugin.dll" },
  "capabilities": ["ReadRadioState", "ControlRadio", "PersistSettings"],
  "ui": {
    "modules": ["ui/midi-settings.es.js"],
    "panels": [
      {
        "id": "midi.settings",
        "title": "MIDI Controllers",
        "icon": "Music",
        "slot": "settings.plugins",
        "category": "controls"
      }
    ]
  }
}
```

### 4.3 Key Architectural Decision: Reuse the TCI Command Surface

Zeus already has a **complete command dispatch layer** in `TciSession.DispatchCommand()` that translates string commands into `RadioService` / `TxService` method calls. This is the same layer that TCI clients (MSHV, WSJT-X, JTDX, SDR#) use to control the radio.

Instead of the MIDI plugin calling REST endpoints (which creates tight coupling to internal DTOs and requires `NetworkAccess`), it calls the **same `RadioService` / `TxService` methods** that TCI already uses. This:

- **Eliminates REST coupling** — no HTTP calls, no DTO imports, no port discovery
- **Eliminates the READY/PARTIAL distinction** — all commands go through the same code path
- **Unlocks commands that were "MISSING" via REST but implemented in TCI** — NB1/NB2 toggle, ANF, SNB, preamp, attenuator (all wired in `TciSession`)
- **Makes the plugin truly self-contained** — only needs `ControlRadio` + `ReadRadioState` capabilities

This requires extracting an **`IRadioCommandSurface`** interface from the methods `TciSession` already calls, then injecting it into the plugin via `IPluginContext`. TciSession and the MIDI plugin both consume the same interface. See §14 (red-light) for the maintainer decision on this refactor.

### 4.4 Core Classes

```
ZeusMidiPlugin : IZeusPlugin, IBackendPlugin
├── InitializeAsync()        → open MIDI engine, load mappings, start listener
├── ShutdownAsync()          → close devices, dispose engine
└── MapEndpoints()           → REST surface for settings UI

MidiEngine (abstraction)
├── DryWetMidiEngine         → Windows + macOS (Melanchall.DryWetMidi)
├── AlsaMidiEngine           → Linux (ALSA seq P/Invoke)
└── NullMidiEngine           → CI / headless / no-MIDI fallback

MidiDispatcher
├── Owns mapping table: (DeviceName, ControlId, ControlType) → ZeusMidiCommand
├── On MIDI event → lookup → call IRadioCommandSurface methods
├── State mirror via IRadioStateReader events (for relative commands)
├── Thread-safe: ConcurrentDictionary for mapping table
└── Learn mode: buffer last event, expose via REST

ZeusMidiCommand (enum)
├── Maps 1:1 to IRadioCommandSurface methods
└── Extensible as the interface grows
```

### 4.5 Data Flow

```
[USB MIDI Controller]
    │
    ▼
[MidiEngine: DryWetMidi / ALSA]     ← OS-level device I/O
    │
    │  MidiEvent { DeviceName, ControlType, ControlId, Value }
    ▼
[MidiDispatcher]                     ← mapping lookup + value normalization
    │
    │  ZeusMidiCommand + normalized value
    ▼
[IRadioCommandSurface]               ← same interface used by TciSession
    │
    ▼
[RadioService / TxService]           ← direct method calls, no HTTP
```

### 4.6 IRadioCommandSurface — Proposed Interface

Extracted from methods `TciSession` already calls on `RadioService` / `TxService`:

```csharp
public interface IRadioCommandSurface
{
    // VFO
    StateDto SetVfo(long hz);
    StateDto Snapshot();

    // Mode / Filter
    StateDto SetMode(RxMode mode);
    StateDto SetFilter(int lowHz, int highHz);
    StateDto SetTxFilter(int lowHz, int highHz);

    // TX
    bool TrySetMox(bool on, out string? error);
    bool TrySetTun(bool on, out string? error);
    void SetDrive(int percent);
    void SetTuneDrive(int percent);

    // Audio
    StateDto SetRxAfGain(double db);

    // AGC
    StateDto SetAgcTop(double topDb);
    StateDto SetAutoAgc(bool enabled);

    // Preamp / Attenuator
    StateDto SetPreamp(bool on);
    StateDto SetAttenuator(HpsdrAtten atten);
    StateDto SetAutoAtt(bool enabled);

    // NR / NB / ANF / SNB
    StateDto SetNr(NrConfig cfg);
    StateDto SetNr4(Nr4ConfigSetRequest req);

    // Display
    StateDto SetZoom(int level);

    // TX extras
    StateDto SetTxMonitor(TxMonitorSetRequest req);
    StateDto SetTwoTone(TwoToneSetRequest req);
    StateDto SetPs(PsControlSetRequest req);

    // Mic
    void SetMicGain(double db);  // via DspPipelineService
}
```

`RadioService` already implements all these methods. The interface extraction is a mechanical refactor — no new logic, no behavior change. `TciSession` switches from `_radio.SetVfo(hz)` to `_commandSurface.SetVfo(hz)`. The MIDI plugin receives `IRadioCommandSurface` via `IPluginContext`.

### 4.5 Plugin REST Endpoints

Exposed via `IBackendPlugin.MapEndpoints()` under `/api/plugins/com.openhpsdr.zeus.midi/`:

| Method | Path | Description |
|---|---|---|
| GET | `devices` | List connected MIDI devices `[{ name, isOpen, mappingCount }]` |
| GET | `mappings` | All mappings for all devices |
| GET | `mappings/{deviceName}` | Mappings for one device |
| PUT | `mappings` | Save/replace mapping set |
| DELETE | `mappings/{deviceName}/{controlId}` | Delete single mapping |
| POST | `learn/start` | Enter learn mode (body: `{ deviceName }`) |
| POST | `learn/stop` | Exit learn mode |
| GET | `learn/last` | Poll last received MIDI event during learn |
| GET | `commands` | List available `ZeusMidiCommand` values with display names |

## 5. MIDI Library

### 5.1 Primary: Melanchall.DryWetMidi

- **NuGet:** `Melanchall.DryWetMidi` v8.0.3 (MIT)
- **Platforms:** Windows (WinMM P/Invoke), macOS (CoreMIDI)
- **Hot-plug:** macOS via `DevicesWatcher`; Windows via polling `InputDevice.GetAll()` on a 2-second timer
- **Linux:** not supported for device I/O

### 5.2 Linux Gap: ALSA Seq Shim

DryWetMidi has no ALSA backend. For Linux, the plugin implements a thin `AlsaMidiEngine` using direct P/Invoke against `libasound.so` (present on all Linux distros with audio).

Input-only surface — ~10 P/Invoke declarations, ~150–200 lines:

| ALSA Function | Purpose |
|---|---|
| `snd_seq_open` | Open sequencer handle |
| `snd_seq_set_client_name` | Identify as "Zeus MIDI" |
| `snd_seq_create_simple_port` | Create input port (WRITE + SUBS_WRITE) |
| `snd_seq_client_info_*` | Enumerate clients |
| `snd_seq_port_info_*` | Enumerate ports |
| `snd_seq_connect_from` | Subscribe to a device's output |
| `snd_seq_event_input` | Blocking read of next MIDI event |
| `snd_seq_close` | Cleanup |

Hot-plug on Linux: poll `snd_seq_client_info` / `snd_seq_port_info` on a 2-second timer (same strategy as Windows).

### 5.3 Engine Selection

```csharp
IMidiEngine engine = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
    ? new AlsaMidiEngine(logger)
    : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? new DryWetMidiEngine(logger)
        : new NullMidiEngine(logger);
```

### 5.4 New Runtime Dependency

`Melanchall.DryWetMidi` is a new NuGet dependency. Per `CLAUDE.md`, this is **red-light** and requires maintainer approval. However, since this is a **plugin** (not core Zeus), the dependency is isolated — operators who don't install the MIDI plugin never download it.

## 6. Command Set (v1)

Since the plugin dispatches through `IRadioCommandSurface` (§4.3), every command that TCI already handles is available. No REST coupling, no READY/PARTIAL distinction. The state mirror (`IRadioStateReader` events) provides current state for relative commands (mode next, filter wider, NR toggle).

### 6.1 Buttons (toggle / momentary)

| ZeusMidiCommand | Thetis Equivalent | IRadioCommandSurface Call | Notes |
|---|---|---|---|
| `Mox` | `MOXOnOff` | `TrySetMox(!current)` | |
| `Tune` | `TunOnOff` | `TrySetTun(!current)` | |
| `TwoTone` | `TwoToneOnOff` | `SetTwoTone(...)` | |
| `PureSignal` | `PSOnOff` | `SetPs(...)` | |
| `Nr1` | `NoiseReductionOnOff` | `SetNr(cfg with NrMode toggled)` | Read state → flip |
| `Nr2` | `NoiseReduction2OnOff` | `SetNr(cfg with NrMode toggled)` | |
| `Nr3` | `NoiseReduction3OnOff` | `SetNr(cfg with NrMode toggled)` | |
| `Nr4` | `NoiseReduction4OnOff` | `SetNr4(...)` | |
| `Nb1` | `Rx1NoiseBlanker1OnOff` | `SetNr(cfg with NbMode toggled)` | **New — was MISSING via REST, wired in TCI** |
| `Nb2` | `Rx1Noiseblanker2OnOff` | `SetNr(cfg with NbMode toggled)` | **New** |
| `Anf` | `AutoNotchOnOff` | `SetNr(cfg with AnfEnabled toggled)` | **New** |
| `Snb` | `SpectralNoiseBlankerOnOff` | `SetNr(cfg with SnbEnabled toggled)` | **New** |
| `AutoAgc` | `RX1AutoAGC` | `SetAutoAgc(!current)` | |
| `AutoAtt` | — (Zeus extra) | `SetAutoAtt(!current)` | |
| `Preamp` | `Rx2PreAmpOnOff` (adapted) | `SetPreamp(!current)` | Toggle variant |
| `ModeNext` | `Rx1ModeNext` | Read mode → compute next → `SetMode(next)` | |
| `ModePrev` | `Rx1ModePrev` | Read mode → compute prev → `SetMode(prev)` | |
| `ModeSsb` | `ModeSSB` | `SetMode(SSB)` | |
| `ModeLsb`..`ModeDrm` | `ModeLSB`..`ModeDRM` | `SetMode(X)` | 13 direct mode selects |
| `FilterWider` | `Rx1FilterWider` | Read filter → widen → `SetFilter(lo, hi)` | |
| `FilterNarrower` | `Rx1FilterNarrower` | Read filter → narrow → `SetFilter(lo, hi)` | |
| `ZoomIn` | `ZoomInc` | `SetZoom(current + step)` | |
| `ZoomOut` | `ZoomDec` | `SetZoom(current - step)` | |
| `VfoAUp100k` | `MoveVFOAUp100Khz` | `SetVfo(current + 100_000)` | |
| `VfoADown100k` | `MoveVFOADown100Khz` | `SetVfo(current - 100_000)` | |
| `BandUp` | `BandUp` | Read band memory → next band → `SetVfo(freq)` + `SetMode(mode)` | |
| `BandDown` | `BandDown` | Same, previous | |
| `Band160m`..`Band2m` | `Band160m`..`Band2m` | Band memory lookup → `SetVfo` + `SetMode` | 12 commands |
| `Mute` | `MuteOnOff` | `SetMute(!current)` | Via native audio sink |

### 6.2 Knobs / Sliders (absolute 0–127 → parameter range)

| ZeusMidiCommand | Thetis Equivalent | IRadioCommandSurface Call | Range |
|---|---|---|---|
| `AfGain` | `SetAFGain` / `VolumeVfoA` | `SetRxAfGain(db)` | -50 to +20 dB |
| `AgcLevel` | `AGCLevel` | `SetAgcTop(db)` | -20 to 120 dB |
| `Drive` | `DriveLevel` | `SetDrive(pct)` | 0–100% |
| `TuneDrive` | `TUNPowerLevel` | `SetTuneDrive(pct)` | 0–100% |
| `MicGain` | `MicGain` | `SetMicGain(db)` | via DspPipelineService |
| `TxMonitor` | `TXAFMonitor` | `SetTxMonitor(...)` | |
| `Nr4Amount` | `NoiseReduction4Amount` | `SetNr4(...)` | |
| `Zoom` | `ZoomSliderFix` | `SetZoom(level)` | |
| `Attenuator` | — (Zeus extra) | `SetAttenuator(HpsdrAtten(db))` | **New — was in TCI, not in Midi2Cat** |

### 6.3 Wheels / Encoders (relative ±delta)

| ZeusMidiCommand | Thetis Equivalent | IRadioCommandSurface Call |
|---|---|---|
| `VfoATune` | `ChangeFreqVfoA` | `SetVfo(current + delta * step)` |
| `VfoAMultiStep` | `MultiStepVfoA` | `SetVfo(current + delta * largeStep)` |
| `FilterBandwidth` | `FilterBandwidth` | Read filter → adjust symmetrically → `SetFilter` |
| `FilterHigh` | `FilterHigh` | Read filter → `SetFilter(lo, hi + delta)` |
| `FilterLow` | `FilterLow` | Read filter → `SetFilter(lo + delta, hi)` |
| `FilterShift` | `FilterShift` | Read filter → shift both → `SetFilter(lo+d, hi+d)` |
| `TxFilterHigh` | `TXFilterHigh` | Read TX filter → `SetTxFilter(lo, hi + delta)` |
| `TxFilterLow` | `TXFilterLow` | Read TX filter → `SetTxFilter(lo + delta, hi)` |
| `ZoomWheel` | `ZoomSliderInc` | `SetZoom(current + delta)` |
| `AfGainWheel` | `VolumeVfoA_inc` | `SetRxAfGain(current + delta)` |
| `AgcLevelWheel` | `AGCLevel_inc` | `SetAgcTop(current + delta)` |
| `DriveWheel` | `DriveLevel_inc` | `SetDrive(current + delta)` |

### 6.4 MIDI-Internal meta-commands

| ZeusMidiCommand | Thetis Equivalent | Description |
|---|---|---|
| `WheelSensUp` | `MidiMessagesPerTuneStepUp` | Increase wheel messages-per-VFO-step |
| `WheelSensDown` | `MidiMessagesPerTuneStepDown` | Decrease |
| `WheelSensToggle` | `MidiMessagesPerTuneStepToggle` | Toggle high/low sensitivity |

### 6.5 Summary

| Category | Count |
|---|---|
| Buttons | ~45 (14 mode + 14 band + 4 NR/NB + 2 ANF/SNB + 2 filter + 2 zoom + 2 VFO jump + TX/mute/agc/preamp/etc.) |
| Knobs | 9 |
| Wheels | 12 |
| Meta | 3 |
| **Total** | **~69** |

Compared to the previous REST-based approach (~62), this adds **NB1, NB2, ANF, SNB, attenuator knob, auto-att toggle, filter shift wheel** — 7 commands that were MISSING before. More importantly, every command goes through one clean interface with no HTTP coupling.

## 7. Mapping Model

### 7.1 Data Structure

```json
{
  "version": 1,
  "devices": {
    "Behringer CMD PL-1": {
      "mappings": [
        {
          "controlId": 16,
          "controlType": "CC",
          "channel": 0,
          "command": "VfoATune",
          "options": {
            "relative": true,
            "stepMultiplier": 1
          }
        },
        {
          "controlId": 34,
          "controlType": "NoteOn",
          "channel": 0,
          "command": "Mox",
          "options": {
            "toggle": true
          }
        }
      ]
    }
  }
}
```

### 7.2 Control Types (MIDI message mapping)

| MIDI Message | Plugin ControlType | Typical Use |
|---|---|---|
| Control Change (0xBn) | `CC` | Knobs, faders, encoders |
| Note On (0x9n) | `NoteOn` | Buttons (velocity as value) |
| Note Off (0x8n) | `NoteOff` | Button release |
| Pitch Bend (0xEn) | `PitchBend` | Pitch wheel / ribbon |

### 7.3 Value Normalization

- **Knobs/Sliders** (absolute CC 0–127): linearly scaled to the target parameter's range (e.g., 0–127 → 0–100% drive).
- **Encoders** (relative CC): interpreted as signed delta. Common encoding conventions supported:
  - **Twos complement** (default): 1–63 = CW (positive delta), 65–127 = CCW (negative delta, i.e. 127 = -1, 126 = -2, …)
  - **Sign-magnitude:** bit 6 is direction flag. 1–63 = CW, 65–127 = CCW (value = 64 + magnitude)
  - **Offset binary:** 64 = center/no motion, >64 = CW, <64 = CCW
  - Configurable per mapping via `options.encoderMode`.
- **Buttons** (NoteOn): toggle or momentary, configurable via `options.toggle`.

### 7.4 Persistence

Mappings stored via `IPluginSettings` (LiteDB-backed, scoped to plugin ID). The entire mapping structure (§7.1) is serialized as a single JSON value under one key (e.g. `"mappings"`), since `IPluginSettings` is a key-value store (`GetAsync<T>(key)` / `SetAsync<T>(key, value)`), not a document database. No filesystem access required — `PersistSettings` capability is granted by default.

## 8. Learn Mode

### 8.1 Flow

1. Operator opens Settings → MIDI Controllers panel.
2. Selects a connected device from the device list.
3. Clicks "Learn" on a target command row (e.g. "VFO A Tune").
4. UI enters learn state: highlights the row, greys out other controls.
5. Operator moves a physical knob/button on the controller.
6. Plugin captures the MIDI event → `POST learn/stop` with the captured `controlId` + `controlType`.
7. UI shows the captured mapping. Operator confirms or retries.
8. Mapping saved to the device's mapping set.

### 8.2 Backend

- `POST learn/start` → sets `_learnDevice` + `_learnCommand`, MIDI listener routes next event to learn buffer instead of dispatch.
- `GET learn/last` → returns the last captured event (polled by UI at ~200ms).
- `POST learn/stop` → clears learn state, returns captured event.

## 9. UI Panel

React component mounted at `settings.plugins` slot. Minimal, functional.

### 9.1 Layout

```
┌─ MIDI Controllers ──────────────────────────────────┐
│                                                      │
│  Devices:  [Behringer CMD PL-1 ▾]  ● Connected      │
│                                                      │
│  ┌─ Mappings ──────────────────────────────────────┐ │
│  │ Control   Type    Command          [Action]     │ │
│  │ CC 16     Wheel   VFO A Tune       [✕] [Learn] │ │
│  │ CC 17     Knob    AF Gain          [✕] [Learn] │ │
│  │ Note 34   Button  MOX (toggle)     [✕] [Learn] │ │
│  │ Note 35   Button  Mute (toggle)    [✕] [Learn] │ │
│  │ ...                                             │ │
│  └─────────────────────────────────────────────────┘ │
│                                                      │
│  [+ Add Mapping]                     [Reset Device]  │
└──────────────────────────────────────────────────────┘
```

### 9.2 "Add Mapping" flow

1. Click "+ Add Mapping".
2. Select target command from dropdown (grouped by category: VFO, Band, Mode, Filter, TX, DSP, Display).
3. Click "Learn" → move the physical control → mapping captured.
4. Optionally configure: toggle mode, encoder mode, step multiplier.
5. Save.

## 10. Platform Support Matrix

| Platform | MIDI Backend | Hot-plug | Native Dependency |
|---|---|---|---|
| Windows x64 | DryWetMidi (WinMM) | Polling (2s timer) | None (managed P/Invoke to winmm.dll) |
| macOS arm64/x64 | DryWetMidi (CoreMIDI) | Native (`DevicesWatcher`) | None (managed P/Invoke to CoreMIDI) |
| Linux x64 | AlsaMidiEngine (ALSA seq) | Polling (2s timer) | `libasound.so` (present on all distros with audio) |
| No MIDI hardware | NullMidiEngine | N/A | None |

## 11. Phased Plan

| Phase | Scope | Effort |
|---|---|---|
| **1 — Plumbing** | Plugin skeleton (`IZeusPlugin` + `IBackendPlugin`). `MidiEngine` abstraction + DryWetMidi integration. Enumerate devices, log incoming events. No mappings, no UI. Verify on Windows + macOS. | ~1 week |
| **2 — Dispatch** | `MidiDispatcher` + `ZeusMidiCommand` enum (full v1 set). Static JSON mapping (hand-edited). Verify VFO tune, drive, MOX, band change work end-to-end. | ~1 week |
| **3 — Linux** | `AlsaMidiEngine` ALSA shim. Verify on Linux. | 2–3 days |
| **4 — UI + Learn** | React settings panel. Learn mode. Save/load via `IPluginSettings`. End-to-end from physical knob to radio command with zero JSON editing. | 1–2 weeks |
| **5 — Polish** | Encoder mode auto-detection heuristic. Wheel sensitivity (messages-per-step). Edge cases (device name collision, rapid CC flood throttling). | ~1 week |
| **6 — Grow** | Add commands as Zeus API surface expands (RIT/XIT, VOX, CW, NB, squelch, etc.). Ongoing, incremental. | As needed |

## 12. Testing

- **`NullMidiEngine`** — unit tests feed synthetic `MidiEvent`s into `MidiDispatcher`, assert correct `IRadioController` calls and REST requests.
- **Integration test:** synthetic CC 16 value 65 → assert `SetFrequencyAsync` called with positive delta.
- **Integration test:** synthetic NoteOn 34 → assert `SetMoxAsync(true)` called.
- **Integration test:** mapping persistence round-trip via `IPluginSettings`.
- **Manual hardware smoke test** on at least one cheap controller (Korg nanoKontrol2 or similar) per platform before shipping Phase 4.

## 13. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| DryWetMidi API changes or abandonment | Engine layer breaks | Thin `IMidiEngine` abstraction — swap library without touching dispatcher or UI |
| ALSA shim edge cases (exotic USB-MIDI adapters) | Linux devices not detected | Start with standard USB class-compliant devices; widen support based on bug reports |
| `IRadioCommandSurface` extraction requires core change | Blocks plugin development | Mechanical refactor — extract interface from existing public methods, no new logic. TciSession is the proof that the surface is stable and correct. Red-light item §14 |
| Mapping table concurrency | MIDI thread reads, UI thread writes | Use `ConcurrentDictionary` or `ReaderWriterLockSlim` for the mapping table. Document thread ownership in code |
| MIDI CC flood (encoder sending 100+ msg/sec) | Overwhelm REST endpoint | Throttle: coalesce rapid CC values, dispatch at max 30 Hz per control |
| Device name string mismatch across OS | Same controller = different name on Win vs. Mac | Match by substring (case-insensitive), same approach as Thetis |

## 14. Items Requiring Maintainer Decision (Red-Light)

1. **New NuGet dependency** — `Melanchall.DryWetMidi` (MIT). Isolated to plugin, not core Zeus. Acceptable?
2. **Plugin ID and ownership** — `com.openhpsdr.zeus.midi` implies official. Ship in the registry or as community plugin?
3. **`IRadioCommandSurface` extraction** — extracting an interface from `RadioService` / `TxService` public methods and exposing it via `IPluginContext`. This is the key enabler: TciSession and the MIDI plugin share the same dispatch surface. Mechanical refactor, no behavior change, but it touches `Zeus.Plugins.Contracts` (new interface) and `Zeus.Server.Hosting` (wiring). Acceptable?
4. **v1 command set** — ~69 commands per §6. Any to add or drop?
5. **Settings panel slot** — `settings.plugins` category `controls`. Correct placement?

## 15. References

- [Thetis Midi2Cat command cross-match](../references/thetis-midi2cat-commands.md)
- [Zeus plugin author guide](../plugins/author-guide.md)
- [Zeus plugin manifest spec](../plugins/manifest-spec.md)
- [Zeus plugin capabilities](../plugins/capabilities.md)
- [Earlier MIDI proposal (superseded)](../proposals/midi.md)
- [Melanchall.DryWetMidi documentation](https://melanchall.github.io/drywetmidi/)
