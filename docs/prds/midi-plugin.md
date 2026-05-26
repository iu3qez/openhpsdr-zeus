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

1. **Thetis command parity on Zeus's existing API surface.** Every Midi2Cat command that Zeus already exposes as a REST/SignalR endpoint must be mappable from a MIDI controller on day one. Per the [cross-match reference](../references/thetis-midi2cat-commands.md), this is **35 commands** (READY — direct 1:1 endpoint mapping) + **27 commands** (PARTIAL — implementable with plugin-side read-modify-write or sequencing logic).
2. **Plugin architecture.** Delivered as a Zeus plugin (`IZeusPlugin` + `IBackendPlugin`), not baked into the core. Installable/removable. Uses the existing plugin system contracts (SDK ABI 1).
3. **Cross-platform.** Windows, macOS, Linux. Input only (no LED output in v1).
4. **Learn mode.** Operator connects a controller, presses "Learn", moves a knob, and assigns it to a Zeus command. No manual MIDI channel/CC editing required.
5. **Persistent mappings.** Saved per-device in the plugin's scoped settings store (`IPluginSettings`). Survive restart.
6. **Hot-plug.** Device connect/disconnect detected at runtime without restart.
7. **Zero disruption to Zeus core.** No changes to `Zeus.Contracts`, `Zeus.Server.Hosting`, or `zeus-web` required — the plugin is self-contained.

## 3. Non-Goals

1. **LED / MIDI output feedback.** The messiest, most device-specific part of Midi2Cat. Deferred to a future version if demand exists.
2. **RX2 commands.** Zeus does not implement RX2. The ~80 RX2/N/A commands from Midi2Cat are out of scope.
3. **Commands Zeus doesn't expose.** The ~98 MISSING commands (RIT/XIT, VFO B, VOX, CW speed, NB toggles, squelch, etc.) cannot be wired until Zeus adds the corresponding API surface. The plugin will grow as Zeus does.
8. **VFO B.** Zeus has no VFO B support (`VfoSetRequest` is VFO A only). VFO B commands are MISSING.
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
  "capabilities": ["ReadRadioState", "ControlRadio", "NetworkAccess", "PersistSettings"],
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

### 4.3 Core Classes

```
ZeusMidiPlugin : IZeusPlugin, IBackendPlugin
├── InitializeAsync()        → open MIDI engine, load mappings, start listener
├── ShutdownAsync()          → close devices, dispose engine
└── MapEndpoints()           → REST surface for UI

MidiEngine (abstraction)
├── DryWetMidiEngine         → Windows + macOS (Melanchall.DryWetMidi)
├── AlsaMidiEngine           → Linux (ALSA seq P/Invoke)
└── NullMidiEngine           → CI / headless / no-MIDI fallback

MidiDispatcher
├── Owns mapping table: (DeviceName, ControlId, ControlType) → ZeusMidiCommand
├── On MIDI event → lookup → call IRadioController / REST endpoint
└── Learn mode: buffer last event, expose via hub

ZeusMidiCommand (enum)
├── 62 radio commands (35 READY + 27 PARTIAL) + 3 meta-commands
└── Extensible as Zeus API surface grows
```

### 4.4 Data Flow

```
[USB MIDI Controller]
    │
    ▼
[MidiEngine: DryWetMidi / ALSA]     ← OS-level device I/O
    │
    │  MidiEvent { DeviceName, ControlType, ControlId, Value }
    ▼
[MidiDispatcher]                     ← mapping lookup
    │
    │  ZeusMidiCommand + normalized value
    ▼
[IRadioController / HTTP calls]      ← same path as SignalR hub
    │
    ▼
[RadioService / TxService / DspPipelineService]
```

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

The v1 command set covers **62 commands**: 35 READY (direct 1:1 endpoint mapping) + 27 PARTIAL (plugin implements sequencing logic). Grouped by control type.

### 6.0 State Mirror

Many PARTIAL commands require knowing the current radio state (current mode for mode-next, current bandwidth for filter-wider, current NR config for NR toggle, etc.). Rather than doing a GET-then-POST for every event, the plugin subscribes to the Zeus WebSocket (`/ws`) at init and maintains a **local state mirror**. This gives zero-latency reads for every dispatch.

### 6.1 READY — Buttons (toggle / momentary)

Direct 1:1 mapping, no state read required.

| ZeusMidiCommand | Thetis Equivalent | Zeus Endpoint |
|---|---|---|
| `Mox` | `MOXOnOff` | `POST /api/tx/mox` |
| `Tune` | `TunOnOff` | `POST /api/tx/tun` |
| `TwoTone` | `TwoToneOnOff` | `POST /api/tx/twotone` |
| `PureSignal` | `PSOnOff` | `POST /api/tx/ps` |
| `Mute` | `MuteOnOff` | `POST /api/audio/native/mute` |
| `Nr4` | `NoiseReduction4OnOff` | `POST /api/rx/nr4` |
| `AutoAgc` | `RX1AutoAGC` | `POST /api/auto-agc` |
| `ModeSsb` | `ModeSSB` | `POST /api/mode` |
| `ModeLsb` | `ModeLSB` | `POST /api/mode` |
| `ModeUsb` | `ModeUSB` | `POST /api/mode` |
| `ModeDsb` | `ModeDSB` | `POST /api/mode` |
| `ModeCw` | `ModeCW` | `POST /api/mode` |
| `ModeCwl` | `ModeCWL` | `POST /api/mode` |
| `ModeCwu` | `ModeCWU` | `POST /api/mode` |
| `ModeFm` | `ModeFM` | `POST /api/mode` |
| `ModeAm` | `ModeAM` | `POST /api/mode` |
| `ModeDigu` | `ModeDIGU` | `POST /api/mode` |
| `ModeDigl` | `ModeDIGL` | `POST /api/mode` |
| `ModeSam` | `ModeSAM` | `POST /api/mode` |
| `ModeSpec` | `ModeSPEC` | `POST /api/mode` |
| `ModeDrm` | `ModeDRM` | `POST /api/mode` |
| `ZoomIn` | `ZoomInc` | `POST /api/rx/zoom` |
| `ZoomOut` | `ZoomDec` | `POST /api/rx/zoom` |
| `VfoADown100k` | `MoveVFOADown100Khz` | read state → `POST /api/vfo` (freq - 100 kHz) |
| `VfoAUp100k` | `MoveVFOAUp100Khz` | read state → `POST /api/vfo` (freq + 100 kHz) |

### 6.2 READY — Knobs / Sliders (absolute 0–127)

| ZeusMidiCommand | Thetis Equivalent | Zeus Endpoint |
|---|---|---|
| `AfGain` | `SetAFGain` / `VolumeVfoA` | `POST /api/rx/afGain` |
| `AgcLevel` | `AGCLevel` | `POST /api/agcGain` |
| `Preamp` | `PreAmpSettingsKnob` | `POST /api/preamp` |
| `Drive` | `DriveLevel` | `POST /api/tx/drive` |
| `TuneDrive` | `TUNPowerLevel` | `POST /api/tx/tune-drive` |
| `MicGain` | `MicGain` | `POST /api/mic-gain` |
| `TxMonitor` | `TXAFMonitor` | `POST /api/tx/monitor` |
| `Nr4Amount` | `NoiseReduction4Amount` | `POST /api/rx/nr4` |
| `Zoom` | `ZoomSliderFix` | `POST /api/rx/zoom` |

### 6.3 READY — Wheels / Encoders (relative ±delta)

| ZeusMidiCommand | Thetis Equivalent | Zeus Endpoint |
|---|---|---|
| `VfoATune` | `ChangeFreqVfoA` | `POST /api/vfo` |
| `VfoAMultiStep` | `MultiStepVfoA` | `POST /api/vfo` (larger step) |
| `FilterBandwidth` | `FilterBandwidth` | `POST /api/bandwidth` |
| `FilterHigh` | `FilterHigh` | `POST /api/filter` |
| `FilterLow` | `FilterLow` | `POST /api/filter` |
| `TxFilterHigh` | `TXFilterHigh` | `POST /api/tx-filter` |
| `TxFilterLow` | `TXFilterLow` | `POST /api/tx-filter` |
| `ZoomWheel` | `ZoomSliderInc` | `POST /api/rx/zoom` |
| `AfGainWheel` | `VolumeVfoA_inc` | `POST /api/rx/afGain` |
| `AgcLevelWheel` | `AGCLevel_inc` | `POST /api/agcGain` |
| `DriveWheel` | `DriveLevel_inc` | `POST /api/tx/drive` |

**Subtotal READY: 35** (25 buttons + 9 knobs + 11 wheels — 10 overlap as wheel variants of knob commands)

### 6.4 PARTIAL — require plugin-side state + sequencing logic

These commands need the state mirror (§6.0) to read current state, compute the target, and POST.

| ZeusMidiCommand | Thetis Equivalent | Type | Logic |
|---|---|---|---|
| `BandUp` | `BandUp` | B | Read band memory → find next band → `POST /api/vfo` + `POST /api/mode` |
| `BandDown` | `BandDown` | B | Same, previous band |
| `Band160m`..`Band2m` | `Band160m`..`Band2m` | B | Read band memory for target band → `POST /api/vfo` + `POST /api/mode` (12 commands) |
| `ModeNext` | `Rx1ModeNext` | B | Read current mode → compute next in sequence → `POST /api/mode` |
| `ModePrev` | `Rx1ModePrev` | B | Same, previous |
| `FilterWider` | `Rx1FilterWider` | B | Read current (low, high) → widen by step → `POST /api/bandwidth` |
| `FilterNarrower` | `Rx1FilterNarrower` | B | Same, narrow |
| `FilterShift` | `FilterShift` | K | Read current (low, high) → shift both edges → `POST /api/filter` |
| `Nr1` | `NoiseReductionOnOff` | B | Read full `NrConfig` → flip NR mode → `POST /api/rx/nr` |
| `Nr2` | `NoiseReduction2OnOff` | B | Same pattern |
| `Nr3` | `NoiseReduction3OnOff` | B | Same pattern |
| `MonOnOff` | `MONOnOff` | B | Toggle via `POST /api/tx/monitor` |
| `DisplayAvg` | `DisplayAverage` | B | Read-modify-write `PUT /api/display-settings` |
| `DisplayPeak` | `DisplayPeak` | B | Same |
| `DisplayTxFilter` | `DisplayTxFilter` | B | Same |
| `WaterfallLow` | `WaterfallLowLimit` | K | Same |
| `WaterfallHigh` | `WaterfallHighLimit` | K | Same |
| `StartStop` | `StartOnOff` | B | `POST /api/connect` or `POST /api/disconnect` based on current state |

**Subtotal PARTIAL: 27** (14 band + 2 mode + 2 filter + 1 filter shift + 3 NR + 1 MON + 3 display + 1 start)

### 6.5 MIDI-Internal meta-commands (plugin-only, no Zeus API)

| ZeusMidiCommand | Thetis Equivalent | Description |
|---|---|---|
| `WheelSensUp` | `MidiMessagesPerTuneStepUp` | Increase wheel messages-per-VFO-step |
| `WheelSensDown` | `MidiMessagesPerTuneStepDown` | Decrease |
| `WheelSensToggle` | `MidiMessagesPerTuneStepToggle` | Toggle high/low sensitivity |

**Grand total v1: 62 radio commands + 3 meta-commands = 65.**

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
| `IRadioController` too narrow (3 methods) | Can't dispatch most commands | Use direct HTTP calls to Zeus REST endpoints from within the plugin. The plugin declares `NetworkAccess` capability. Port discovered from ASP.NET config (not hardcoded). `IRadioController` is a convenience for freq/mode/MOX, not the only path. Long-term: propose expanding `IRadioController` to cover the top ~15 commands (drive, filter, band, NR, zoom) to reduce coupling to internal DTOs |
| Mapping table concurrency | MIDI thread reads, UI thread writes | Use `ConcurrentDictionary` or `ReaderWriterLockSlim` for the mapping table. Document thread ownership in code |
| MIDI CC flood (encoder sending 100+ msg/sec) | Overwhelm REST endpoint | Throttle: coalesce rapid CC values, dispatch at max 30 Hz per control |
| Device name string mismatch across OS | Same controller = different name on Win vs. Mac | Match by substring (case-insensitive), same approach as Thetis |

## 14. Items Requiring Maintainer Decision (Red-Light)

1. **New NuGet dependency** — `Melanchall.DryWetMidi` (MIT). Isolated to plugin, not core Zeus. Acceptable?
2. **Plugin ID and ownership** — `com.openhpsdr.zeus.midi` implies official. Ship in the registry or as community plugin?
3. **IRadioController expansion** — currently only `SetFrequencyAsync`, `SetModeAsync`, `SetMoxAsync`. Should the plugin SDK grow to cover band, filter, drive, etc.? Or should the MIDI plugin call Zeus REST directly?
4. **v1 command set** — the ~55 READY commands per §6. Any to add or drop?
5. **Settings panel slot** — `settings.plugins` category `controls`. Correct placement?
6. **`IRadioController` expansion** — currently 3 methods (`SetFrequencyAsync`, `SetModeAsync`, `SetMoxAsync`). The MIDI plugin needs ~52 more commands via direct HTTP. Should the plugin SDK grow to cover drive, filter, band, NR, zoom, etc.? This reduces coupling to internal Zeus DTOs and makes third-party plugins more robust against endpoint changes.

## 15. References

- [Thetis Midi2Cat command cross-match](../references/thetis-midi2cat-commands.md)
- [Zeus plugin author guide](../plugins/author-guide.md)
- [Zeus plugin manifest spec](../plugins/manifest-spec.md)
- [Zeus plugin capabilities](../plugins/capabilities.md)
- [Earlier MIDI proposal (superseded)](../proposals/midi.md)
- [Melanchall.DryWetMidi documentation](https://melanchall.github.io/drywetmidi/)
