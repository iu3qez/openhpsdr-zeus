# MIDI Controller Plugin — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cross-platform MIDI controller plugin for Zeus that maps USB MIDI controllers to radio commands via `IRadioCommandSurface`.

**Architecture:** Phase 0 extracts `IRadioCommandSurface` from `RadioService`/`TxService` and wires it into the plugin DI system (ABI 2). Phases 1–2 build the plugin itself: a `MidiEngine` abstraction (DryWetMidi for Win/macOS, NullMidiEngine for tests), a `MidiDispatcher` that maps MIDI events to `IRadioCommandSurface` calls, and REST endpoints for configuration. Phase 4 adds a React learn-mode UI.

**Tech Stack:** .NET 10, xUnit, Melanchall.DryWetMidi (MIT), React (settings panel ESM), Zeus plugin system (ABI 2)

**Source PRD:** `docs/prds/midi-plugin.md`

---

## File Map

### Phase 0 — Core Zeus: IRadioCommandSurface (pre-requisite, red-light)

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Zeus.Plugins.Contracts/IRadioCommandSurface.cs` | New interface — 22 methods, uses types from `Zeus.Contracts` |
| Modify | `Zeus.Plugins.Contracts/Zeus.Plugins.Contracts.csproj` | Add `ProjectReference` to `Zeus.Contracts` |
| Modify | `Zeus.Plugins.Contracts/IPluginContext.cs` | Add `IRadioCommandSurface? CommandSurface` property; keep old `IRadioController?` as `[Obsolete]` |
| Modify | `Zeus.Plugins.Contracts/AbiVersion.cs` | Bump `Current` from 1 → 2 |
| Create | `Zeus.Server.Hosting/RadioCommandSurfaceAdapter.cs` | Wraps `RadioService` + `TxService`, implements `IRadioCommandSurface` |
| Modify | `Zeus.Server.Hosting/ZeusHost.cs` | Register `IRadioCommandSurface` in DI |
| Modify | `Zeus.Plugins.Host/PluginContext.cs` | Accept + expose `IRadioCommandSurface?` |
| Modify | `Zeus.Plugins.Host/PluginManager.cs` | Resolve `IRadioCommandSurface` from DI, pass to context |
| Create | `tests/Zeus.Server.Tests/RadioCommandSurfaceAdapterTests.cs` | Verify adapter delegates correctly |
| Modify | `tests/Zeus.Plugins.Contracts.Tests/AbiVersionTests.cs` | Update expected version |

### Phase 1 — Plugin Skeleton + MIDI Engine

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `plugins/com.openhpsdr.zeus.midi/plugin.json` | Manifest declaring capabilities |
| Create | `Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj` | Plugin project (references `Zeus.Plugins.Contracts`) |
| Create | `Zeus.Plugins.Midi/ZeusMidiPlugin.cs` | `IZeusPlugin` + `IBackendPlugin` entry point |
| Create | `Zeus.Plugins.Midi/Midi/IMidiEngine.cs` | Engine abstraction: events, device enumeration, open/close |
| Create | `Zeus.Plugins.Midi/Midi/MidiEvent.cs` | `record MidiEvent(string DeviceName, MidiControlType ControlType, int Channel, int ControlId, int Value)` |
| Create | `Zeus.Plugins.Midi/Midi/NullMidiEngine.cs` | Test double: feed synthetic events |
| Create | `Zeus.Plugins.Midi/Midi/DryWetMidiEngine.cs` | Win/macOS real MIDI I/O via DryWetMidi |
| Create | `tests/Zeus.Plugins.Midi.Tests/Zeus.Plugins.Midi.Tests.csproj` | Test project |
| Create | `tests/Zeus.Plugins.Midi.Tests/NullMidiEngineTests.cs` | Verify synthetic event injection |
| Modify | `Zeus.slnx` | Add new projects to solution |

### Phase 2 — Dispatch + Mapping

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Zeus.Plugins.Midi/ZeusMidiCommand.cs` | Enum of all ~69 commands |
| Create | `Zeus.Plugins.Midi/Mapping/MidiMapping.cs` | Mapping data model + JSON serialization |
| Create | `Zeus.Plugins.Midi/Mapping/MidiMappingSet.cs` | Per-device mapping collection + lookup |
| Create | `Zeus.Plugins.Midi/Dispatch/MidiDispatcher.cs` | Core: MIDI event → command lookup → IRadioCommandSurface call |
| Create | `Zeus.Plugins.Midi/Dispatch/ValueNormalizer.cs` | Knob (0–127 → range), encoder (relative delta), button (toggle/momentary) |
| Create | `tests/Zeus.Plugins.Midi.Tests/MidiDispatcherTests.cs` | Dispatch end-to-end with mock surface |
| Create | `tests/Zeus.Plugins.Midi.Tests/ValueNormalizerTests.cs` | Normalization logic |
| Create | `tests/Zeus.Plugins.Midi.Tests/MidiMappingSerializationTests.cs` | JSON round-trip |

### Phase 3 — Linux ALSA (outline)

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Zeus.Plugins.Midi/Midi/AlsaMidiEngine.cs` | ALSA seq P/Invoke shim (~200 lines) |

### Phase 4 — UI + Learn Mode

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Zeus.Plugins.Midi/Learn/LearnSession.cs` | Learn mode state machine |
| Create | `tests/Zeus.Plugins.Midi.Tests/LearnSessionTests.cs` | Learn mode tests |
| Create | `Zeus.Plugins.Midi/ui/midi-settings.tsx` | React settings panel (ESM) |

### Phase 5 — Polish (outline)

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Zeus.Plugins.Midi/Dispatch/CcThrottle.cs` | Coalesce rapid CC events (max 30 Hz per control) |
| Modify | `Zeus.Plugins.Midi/Dispatch/MidiDispatcher.cs` | Integrate throttle + encoder mode auto-detection |

---

## Phase 0: Core Zeus — IRadioCommandSurface

### Task 1: Define IRadioCommandSurface interface

**Files:**
- Create: `Zeus.Plugins.Contracts/IRadioCommandSurface.cs`
- Modify: `Zeus.Plugins.Contracts/Zeus.Plugins.Contracts.csproj`

- [ ] **Step 1: Add Zeus.Contracts project reference to plugin contracts**

```xml
<!-- Zeus.Plugins.Contracts/Zeus.Plugins.Contracts.csproj — add inside <Project> -->
<ItemGroup>
  <ProjectReference Include="..\Zeus.Contracts\Zeus.Contracts.csproj" />
</ItemGroup>
```

This is needed because `IRadioCommandSurface` uses `StateDto`, `RxMode`, `NrConfig`, `Nr4ConfigSetRequest`, `TxMonitorSetRequest`, `TwoToneSetRequest`, `PsControlSetRequest` — all defined in `Zeus.Contracts/Dtos.cs`.

- [ ] **Step 2: Create the interface file**

```csharp
// Zeus.Plugins.Contracts/IRadioCommandSurface.cs
using Zeus.Contracts;

namespace Zeus.Plugins.Contracts;

/// <summary>
/// Full radio command surface for plugins that declare
/// <see cref="PluginCapabilities.ControlRadio"/>.
/// Superset of <see cref="IRadioController"/>; replaces it from ABI 2.
///
/// Every method matches a public method on RadioService or TxService.
/// The adapter (<c>RadioCommandSurfaceAdapter</c> in Zeus.Server.Hosting)
/// delegates to those services directly — no HTTP, no DTO marshaling.
/// </summary>
public interface IRadioCommandSurface
{
    // ── State ────────────────────────────────────────────────────────
    StateDto Snapshot();

    // ── VFO ──────────────────────────────────────────────────────────
    StateDto SetVfo(long hz);

    // ── Mode / Filter ────────────────────────────────────────────────
    StateDto SetMode(RxMode mode);
    StateDto SetFilter(int lowHz, int highHz);
    StateDto SetTxFilter(int lowHz, int highHz);

    // ── TX ───────────────────────────────────────────────────────────
    bool TrySetMox(bool on, out string? error);
    bool TrySetTun(bool on, out string? error);
    void SetDrive(int percent);
    void SetTuneDrive(int percent);

    // ── RX Audio ─────────────────────────────────────────────────────
    StateDto SetRxAfGain(double db);

    // ── AGC ──────────────────────────────────────────────────────────
    StateDto SetAgcTop(double topDb);
    StateDto SetAutoAgc(bool enabled);

    // ── Preamp / Attenuator ──────────────────────────────────────────
    StateDto SetPreamp(bool on);
    StateDto SetAttenuator(int db);
    StateDto SetAutoAtt(bool enabled);

    // ── Noise Reduction ──────────────────────────────────────────────
    StateDto SetNr(NrConfig cfg);
    StateDto SetNr4(Nr4ConfigSetRequest req);

    // ── Display ──────────────────────────────────────────────────────
    StateDto SetZoom(int level);

    // ── TX extras ────────────────────────────────────────────────────
    StateDto SetTxMonitor(TxMonitorSetRequest req);
    StateDto SetTwoTone(TwoToneSetRequest req);
    StateDto SetPs(PsControlSetRequest req);

    // ── Mic ──────────────────────────────────────────────────────────
    StateDto SetTxMicGain(int db);
}
```

Design notes for the implementer:
- `SetAttenuator(int db)` takes a plain `int` (not `HpsdrAtten`) to avoid exposing `Zeus.Protocol1` types to plugin authors. The adapter creates `new HpsdrAtten(db)` internally.
- `SetTxMicGain(int db)` matches `RadioService.SetTxMicGain(int db)` — clamped to [-40, +10] on the server side.
- `TrySetMox`/`TrySetTun` delegate to `TxService` (which tracks MOX ownership), not `RadioService.SetMox`.

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Zeus.slnx`
Expected: Build succeeds. No consumer references the new interface yet.

- [ ] **Step 4: Commit**

```bash
git add Zeus.Plugins.Contracts/IRadioCommandSurface.cs Zeus.Plugins.Contracts/Zeus.Plugins.Contracts.csproj
git commit -m "feat(plugins): define IRadioCommandSurface interface (ABI 2 prep)"
```

---

### Task 2: Update IPluginContext and ABI version

**Files:**
- Modify: `Zeus.Plugins.Contracts/IPluginContext.cs`
- Modify: `Zeus.Plugins.Contracts/AbiVersion.cs`
- Modify: `tests/Zeus.Plugins.Contracts.Tests/AbiVersionTests.cs`

- [ ] **Step 1: Add CommandSurface to IPluginContext**

In `Zeus.Plugins.Contracts/IPluginContext.cs`, add a new property and mark the old one obsolete:

```csharp
    /// <summary>
    /// Full radio command surface. Null if
    /// <see cref="PluginCapabilities.ControlRadio"/> was not granted.
    /// Replaces <see cref="RadioController"/> from ABI 2.
    /// </summary>
    IRadioCommandSurface? CommandSurface { get; }

    /// <summary>
    /// Mutating radio controller. Null if
    /// <see cref="PluginCapabilities.ControlRadio"/> was not granted.
    /// </summary>
    [Obsolete("Use CommandSurface (ABI 2). RadioController will be removed in ABI 3.")]
    IRadioController? RadioController { get; }
```

- [ ] **Step 2: Bump ABI version**

In `Zeus.Plugins.Contracts/AbiVersion.cs`, change:

```csharp
public const int Current = 2;
```

- [ ] **Step 3: Update ABI version test**

Read `tests/Zeus.Plugins.Contracts.Tests/AbiVersionTests.cs` and update the expected value from 1 to 2. The test will look something like:

```csharp
Assert.Equal(2, AbiVersion.Current);
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build Zeus.slnx && dotnet test tests/Zeus.Plugins.Contracts.Tests/`
Expected: Build succeeds. The `PluginContext` class in `Zeus.Plugins.Host` will fail to compile because it doesn't implement `CommandSurface` yet — that's expected; we fix it in Task 3.

Actually — check if the build breaks. If it does, that's fine, we fix it in the next task. If it compiles (because `PluginContext` doesn't need to implement the new member until wired), even better.

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Contracts/IPluginContext.cs Zeus.Plugins.Contracts/AbiVersion.cs tests/Zeus.Plugins.Contracts.Tests/AbiVersionTests.cs
git commit -m "feat(plugins): add CommandSurface to IPluginContext, bump ABI to 2"
```

---

### Task 3: Implement RadioCommandSurfaceAdapter

**Files:**
- Create: `Zeus.Server.Hosting/RadioCommandSurfaceAdapter.cs`
- Test: `tests/Zeus.Server.Tests/RadioCommandSurfaceAdapterTests.cs`

- [ ] **Step 1: Write the adapter test**

The adapter wraps `RadioService` + `TxService`. Test with real `RadioService` (via `WebApplicationFactory`) or a thin manual mock. Since `RadioService` is a concrete sealed class (not easily mockable), use the integration test pattern from `MicGainEndpointTests.cs` — spin up a `WebApplicationFactory<Program>`, resolve the real `RadioService` from DI, and call through the adapter.

Alternatively, test a subset of delegation via targeted assertions:

```csharp
// tests/Zeus.Server.Tests/RadioCommandSurfaceAdapterTests.cs
using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Server.Hosting;

namespace Zeus.Server.Tests;

public class RadioCommandSurfaceAdapterTests : IClassFixture<RadioCommandSurfaceAdapterTests.Factory>
{
    private readonly Factory _factory;

    public RadioCommandSurfaceAdapterTests(Factory factory) => _factory = factory;

    [Fact]
    public void Snapshot_ReturnsCurrentState()
    {
        var (surface, radio, _) = _factory.CreateSurface();
        var state = surface.Snapshot();
        Assert.Equal(radio.Snapshot().VfoHz, state.VfoHz);
    }

    [Fact]
    public void SetVfo_DelegatesToRadioService()
    {
        var (surface, radio, _) = _factory.CreateSurface();
        var result = surface.SetVfo(14_200_000);
        Assert.Equal(14_200_000, result.VfoHz);
        Assert.Equal(14_200_000, radio.Snapshot().VfoHz);
    }

    [Fact]
    public void SetDrive_ClampsAndDelegates()
    {
        var (surface, radio, _) = _factory.CreateSurface();
        surface.SetDrive(50);
        Assert.Equal(50, radio.Snapshot().DrivePct);
    }

    [Fact]
    public void SetAttenuator_ConvertsIntToHpsdrAtten()
    {
        var (surface, radio, _) = _factory.CreateSurface();
        var result = surface.SetAttenuator(15);
        Assert.Equal(15, result.AttenDb);
    }

    [Fact]
    public void SetMode_DelegatesToRadioService()
    {
        var (surface, _, _) = _factory.CreateSurface();
        var result = surface.SetMode(RxMode.CWU);
        Assert.Equal(RxMode.CWU, result.Mode);
    }

    [Fact]
    public void TrySetMox_DelegatesToTxService()
    {
        var (surface, _, _) = _factory.CreateSurface();
        // Without a connected radio, MOX may fail — that's fine,
        // we're testing delegation, not radio state.
        var ok = surface.TrySetMox(true, out var error);
        // TxService returns false when no radio connected — assert we got a result either way.
        Assert.True(ok || error is not null);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove hosted services that need real hardware
                services.RemoveAll<IHostedService>();
            });
        }

        public (IRadioCommandSurface surface, RadioService radio, TxService tx) CreateSurface()
        {
            var sp = Services;
            var radio = sp.GetRequiredService<RadioService>();
            var tx = sp.GetRequiredService<TxService>();
            return (new RadioCommandSurfaceAdapter(radio, tx), radio, tx);
        }
    }
}
```

Note: adjust imports and `RemoveAll` pattern to match what `MicGainEndpointTests.cs` does. Read that file first for the exact test factory pattern used in this codebase.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Zeus.Server.Tests/ --filter RadioCommandSurfaceAdapter`
Expected: FAIL — `RadioCommandSurfaceAdapter` doesn't exist yet.

- [ ] **Step 3: Implement the adapter**

```csharp
// Zeus.Server.Hosting/RadioCommandSurfaceAdapter.cs
using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Protocol1;

namespace Zeus.Server.Hosting;

internal sealed class RadioCommandSurfaceAdapter : IRadioCommandSurface
{
    private readonly RadioService _radio;
    private readonly TxService _tx;

    public RadioCommandSurfaceAdapter(RadioService radio, TxService tx)
    {
        _radio = radio;
        _tx = tx;
    }

    public StateDto Snapshot() => _radio.Snapshot();
    public StateDto SetVfo(long hz) => _radio.SetVfo(hz);
    public StateDto SetMode(RxMode mode) => _radio.SetMode(mode);
    public StateDto SetFilter(int lowHz, int highHz) => _radio.SetFilter(lowHz, highHz);
    public StateDto SetTxFilter(int lowHz, int highHz) => _radio.SetTxFilter(lowHz, highHz);

    public bool TrySetMox(bool on, out string? error) => _tx.TrySetMox(on, out error);
    public bool TrySetTun(bool on, out string? error) => _tx.TrySetTun(on, out error);
    public void SetDrive(int percent) => _radio.SetDrive(percent);
    public void SetTuneDrive(int percent) => _radio.SetTuneDrive(percent);

    public StateDto SetRxAfGain(double db) => _radio.SetRxAfGain(db);
    public StateDto SetAgcTop(double topDb) => _radio.SetAgcTop(topDb);
    public StateDto SetAutoAgc(bool enabled) => _radio.SetAutoAgc(enabled);

    public StateDto SetPreamp(bool on) => _radio.SetPreamp(on);
    public StateDto SetAttenuator(int db) => _radio.SetAttenuator(new HpsdrAtten(db));
    public StateDto SetAutoAtt(bool enabled) => _radio.SetAutoAtt(enabled);

    public StateDto SetNr(NrConfig cfg) => _radio.SetNr(cfg);
    public StateDto SetNr4(Nr4ConfigSetRequest req) => _radio.SetNr4(req);

    public StateDto SetZoom(int level) => _radio.SetZoom(level);

    public StateDto SetTxMonitor(TxMonitorSetRequest req) => _radio.SetTxMonitor(req);
    public StateDto SetTwoTone(TwoToneSetRequest req) => _radio.SetTwoTone(req);
    public StateDto SetPs(PsControlSetRequest req) => _radio.SetPs(req);

    public StateDto SetTxMicGain(int db) => _radio.SetTxMicGain(db);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Zeus.Server.Tests/ --filter RadioCommandSurfaceAdapter`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Zeus.Server.Hosting/RadioCommandSurfaceAdapter.cs tests/Zeus.Server.Tests/RadioCommandSurfaceAdapterTests.cs
git commit -m "feat(plugins): implement RadioCommandSurfaceAdapter wrapping RadioService + TxService"
```

---

### Task 4: Wire into DI and PluginManager

**Files:**
- Modify: `Zeus.Server.Hosting/ZeusHost.cs` (DI registration)
- Modify: `Zeus.Plugins.Host/PluginContext.cs` (add CommandSurface property)
- Modify: `Zeus.Plugins.Host/PluginManager.cs` (resolve and inject)

- [ ] **Step 1: Register IRadioCommandSurface in DI**

In `Zeus.Server.Hosting/ZeusHost.cs`, after the `RadioService` and `TxService` singleton registrations (~line 170 and ~line 244), add:

```csharp
builder.Services.AddSingleton<IRadioCommandSurface>(sp =>
    new RadioCommandSurfaceAdapter(
        sp.GetRequiredService<RadioService>(),
        sp.GetRequiredService<TxService>()));
```

You'll need a `using Zeus.Plugins.Contracts;` at the top of the file.

- [ ] **Step 2: Update PluginContext to accept and expose CommandSurface**

In `Zeus.Plugins.Host/PluginContext.cs`, add the new constructor parameter and property:

```csharp
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        string pluginId,
        PluginManifest manifest,
        string pluginRootPath,
        PluginCapabilities granted,
        ILogger logger,
        IPluginSettings settings,
        IRadioStateReader? radio,
        IRadioController? radioController,
        IRadioCommandSurface? commandSurface)
    {
        PluginId = pluginId;
        Manifest = manifest;
        PluginRootPath = pluginRootPath;
        GrantedCapabilities = granted;
        Logger = logger;
        Settings = settings;
        Radio = radio;
        RadioController = radioController;
        CommandSurface = commandSurface;
    }

    public string PluginId { get; }
    public PluginManifest Manifest { get; }
    public ILogger Logger { get; }
    public string PluginRootPath { get; }
    public PluginCapabilities GrantedCapabilities { get; }
    public IPluginSettings Settings { get; }
    public IRadioStateReader? Radio { get; }
    public IRadioController? RadioController { get; }
    public IRadioCommandSurface? CommandSurface { get; }
}
```

- [ ] **Step 3: Update PluginManager to resolve and pass IRadioCommandSurface**

In `Zeus.Plugins.Host/PluginManager.cs`, in the `ActivateAsync` method (around line 121–133), update the `PluginContext` construction:

```csharp
var ctx = new PluginContext(
    pluginId: id,
    manifest: loaded.Manifest,
    pluginRootPath: pluginDir,
    granted: granted,
    logger: pluginLogger,
    settings: _settings.ForPlugin(id),
    radio: granted.HasFlag(PluginCapabilities.ReadRadioState)
        ? _services.GetService<IRadioStateReader>()
        : null,
    radioController: granted.HasFlag(PluginCapabilities.ControlRadio)
        ? _services.GetService<IRadioController>()
        : null,
    commandSurface: granted.HasFlag(PluginCapabilities.ControlRadio)
        ? _services.GetService<IRadioCommandSurface>()
        : null);
```

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build Zeus.slnx && dotnet test Zeus.slnx`
Expected: Build succeeds, all tests pass. The existing `PluginManager` tests may need a minor fix if they construct `PluginContext` directly — add the new `commandSurface: null` argument.

- [ ] **Step 5: Fix any PluginManager test compilation errors**

Check `tests/Zeus.Plugins.Host.Tests/PluginManagerTests.cs` and any other files that construct `PluginContext`. Add the `commandSurface: null` parameter.

- [ ] **Step 6: Commit**

```bash
git add Zeus.Server.Hosting/ZeusHost.cs Zeus.Plugins.Host/PluginContext.cs Zeus.Plugins.Host/PluginManager.cs
git add -u tests/  # any test fixes
git commit -m "feat(plugins): wire IRadioCommandSurface into DI and plugin activation"
```

---

### Task 5: Verify end-to-end plugin activation with CommandSurface

**Files:**
- Modify: `tests/Zeus.Plugins.Host.Tests/EndToEndPluginTests.cs`

- [ ] **Step 1: Read the existing end-to-end test**

Read `tests/Zeus.Plugins.Host.Tests/EndToEndPluginTests.cs` to understand the test fixtures (HelloWorld plugin, Amplifier plugin).

- [ ] **Step 2: Add a test that verifies CommandSurface is injected**

Add a test plugin inline that declares `ControlRadio` and asserts it receives a non-null `CommandSurface`:

```csharp
[Fact]
public async Task Plugin_WithControlRadio_ReceivesCommandSurface()
{
    // Create a test plugin that captures the context
    IPluginContext? captured = null;
    // ... (follow the existing test fixture pattern for creating
    //      a plugin directory with manifest + assembly, then
    //      activating it through PluginManager)
    Assert.NotNull(captured?.CommandSurface);
}
```

Adapt this to match the exact test fixture pattern in the file. The key assertion: when a plugin declares `ControlRadio` and `IRadioCommandSurface` is registered in DI, the context's `CommandSurface` is not null.

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/Zeus.Plugins.Host.Tests/ --filter CommandSurface`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add tests/Zeus.Plugins.Host.Tests/EndToEndPluginTests.cs
git commit -m "test(plugins): verify CommandSurface injection in end-to-end plugin test"
```

---

## Phase 1: Plugin Skeleton + MIDI Engine

### Task 6: Create plugin project and manifest

**Files:**
- Create: `Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj`
- Create: `plugins/com.openhpsdr.zeus.midi/plugin.json`
- Modify: `Zeus.slnx`

- [ ] **Step 1: Create the plugin project**

```xml
<!-- Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Zeus.Plugins.Midi</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Zeus.Plugins.Contracts\Zeus.Plugins.Contracts.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create plugin.json manifest**

```json
{
  "schemaVersion": 1,
  "id": "com.openhpsdr.zeus.midi",
  "name": "MIDI Controller",
  "version": "1.0.0",
  "author": "OpenHPSDR Zeus contributors",
  "description": "Map USB MIDI controllers to radio commands (VFO, band, mode, filter, TX, DSP)",
  "license": "GPL-2.0-or-later",
  "sdk": { "abi": 2, "minVersion": "1.0.0" },
  "entrypoint": { "assembly": "Zeus.Plugins.Midi.dll" },
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

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln Zeus.slnx add Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj
```

- [ ] **Step 4: Build**

Run: `dotnet build Zeus.slnx`
Expected: Succeeds (empty project, no source files yet).

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Midi/ plugins/com.openhpsdr.zeus.midi/plugin.json Zeus.slnx
git commit -m "feat(midi): create plugin project skeleton and manifest"
```

---

### Task 7: IMidiEngine abstraction and MidiEvent

**Files:**
- Create: `Zeus.Plugins.Midi/Midi/MidiEvent.cs`
- Create: `Zeus.Plugins.Midi/Midi/MidiControlType.cs`
- Create: `Zeus.Plugins.Midi/Midi/MidiDeviceInfo.cs`
- Create: `Zeus.Plugins.Midi/Midi/IMidiEngine.cs`

- [ ] **Step 1: Define MIDI types**

```csharp
// Zeus.Plugins.Midi/Midi/MidiControlType.cs
namespace Zeus.Plugins.Midi.Midi;

public enum MidiControlType : byte
{
    CC,         // Control Change (0xBn)
    NoteOn,     // Note On (0x9n)
    NoteOff,    // Note Off (0x8n)
    PitchBend,  // Pitch Bend (0xEn)
}
```

```csharp
// Zeus.Plugins.Midi/Midi/MidiEvent.cs
namespace Zeus.Plugins.Midi.Midi;

public sealed record MidiEvent(
    string DeviceName,
    MidiControlType ControlType,
    int Channel,
    int ControlId,
    int Value);
```

```csharp
// Zeus.Plugins.Midi/Midi/MidiDeviceInfo.cs
namespace Zeus.Plugins.Midi.Midi;

public sealed record MidiDeviceInfo(string Name, bool IsOpen);
```

- [ ] **Step 2: Define engine interface**

```csharp
// Zeus.Plugins.Midi/Midi/IMidiEngine.cs
namespace Zeus.Plugins.Midi.Midi;

public interface IMidiEngine : IAsyncDisposable
{
    IReadOnlyList<MidiDeviceInfo> GetDevices();
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    event Action<MidiEvent>? EventReceived;
    event Action<string>? DeviceConnected;
    event Action<string>? DeviceDisconnected;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Zeus.Plugins.Midi/`
Expected: Succeeds.

- [ ] **Step 4: Commit**

```bash
git add Zeus.Plugins.Midi/Midi/
git commit -m "feat(midi): define IMidiEngine, MidiEvent, and supporting types"
```

---

### Task 8: NullMidiEngine (test double)

**Files:**
- Create: `Zeus.Plugins.Midi/Midi/NullMidiEngine.cs`
- Create: `tests/Zeus.Plugins.Midi.Tests/Zeus.Plugins.Midi.Tests.csproj`
- Create: `tests/Zeus.Plugins.Midi.Tests/NullMidiEngineTests.cs`

- [ ] **Step 1: Write the test**

```csharp
// tests/Zeus.Plugins.Midi.Tests/NullMidiEngineTests.cs
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class NullMidiEngineTests
{
    [Fact]
    public void GetDevices_ReturnsEmpty()
    {
        using var engine = new NullMidiEngine();
        Assert.Empty(engine.GetDevices());
    }

    [Fact]
    public async Task InjectEvent_RaisesEventReceived()
    {
        using var engine = new NullMidiEngine();
        await engine.StartAsync(CancellationToken.None);

        MidiEvent? received = null;
        engine.EventReceived += e => received = e;

        var ev = new MidiEvent("TestDevice", MidiControlType.CC, 0, 16, 65);
        engine.InjectEvent(ev);

        Assert.NotNull(received);
        Assert.Equal("TestDevice", received.DeviceName);
        Assert.Equal(MidiControlType.CC, received.ControlType);
        Assert.Equal(16, received.ControlId);
        Assert.Equal(65, received.Value);
    }

    [Fact]
    public async Task InjectEvent_WhenStopped_DoesNotRaise()
    {
        using var engine = new NullMidiEngine();
        // Don't start — events should not fire
        MidiEvent? received = null;
        engine.EventReceived += e => received = e;

        engine.InjectEvent(new MidiEvent("Test", MidiControlType.CC, 0, 1, 64));
        Assert.Null(received);
    }
}
```

- [ ] **Step 2: Create test project**

```xml
<!-- tests/Zeus.Plugins.Midi.Tests/Zeus.Plugins.Midi.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Zeus.Plugins.Midi\Zeus.Plugins.Midi.csproj" />
  </ItemGroup>

</Project>
```

Add to solution:
```bash
dotnet sln Zeus.slnx add tests/Zeus.Plugins.Midi.Tests/Zeus.Plugins.Midi.Tests.csproj --solution-folder /tests
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/`
Expected: FAIL — `NullMidiEngine` doesn't exist.

- [ ] **Step 4: Implement NullMidiEngine**

```csharp
// Zeus.Plugins.Midi/Midi/NullMidiEngine.cs
namespace Zeus.Plugins.Midi.Midi;

public sealed class NullMidiEngine : IMidiEngine
{
    private volatile bool _running;

    public IReadOnlyList<MidiDeviceInfo> GetDevices() => [];

    public Task StartAsync(CancellationToken ct)
    {
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        return Task.CompletedTask;
    }

    public event Action<MidiEvent>? EventReceived;
    public event Action<string>? DeviceConnected;
    public event Action<string>? DeviceDisconnected;

    public void InjectEvent(MidiEvent ev)
    {
        if (_running) EventReceived?.Invoke(ev);
    }

    public void InjectDeviceConnected(string name)
    {
        if (_running) DeviceConnected?.Invoke(name);
    }

    public void InjectDeviceDisconnected(string name)
    {
        if (_running) DeviceDisconnected?.Invoke(name);
    }

    public ValueTask DisposeAsync()
    {
        _running = false;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add Zeus.Plugins.Midi/Midi/NullMidiEngine.cs tests/Zeus.Plugins.Midi.Tests/ Zeus.slnx
git commit -m "feat(midi): implement NullMidiEngine test double with event injection"
```

---

### Task 9: ZeusMidiPlugin entry point

**Files:**
- Create: `Zeus.Plugins.Midi/ZeusMidiPlugin.cs`

- [ ] **Step 1: Implement the plugin entry point**

```csharp
// Zeus.Plugins.Midi/ZeusMidiPlugin.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi;

public sealed class ZeusMidiPlugin : IZeusPlugin, IBackendPlugin
{
    private ILogger _log = null!;
    private IRadioCommandSurface? _surface;
    private IPluginSettings _settings = null!;
    private IMidiEngine _engine = null!;

    public async Task InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _log = context.Logger;
        _surface = context.CommandSurface;
        _settings = context.Settings;
        _engine = CreateEngine();

        await _engine.StartAsync(ct).ConfigureAwait(false);
        _log.LogInformation("MIDI plugin initialized ({DeviceCount} devices)",
            _engine.GetDevices().Count);
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        await _engine.StopAsync(ct).ConfigureAwait(false);
        await _engine.DisposeAsync().ConfigureAwait(false);
        _log.LogInformation("MIDI plugin shut down");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("devices", () =>
            Results.Ok(_engine.GetDevices()));
    }

    internal IMidiEngine EngineForTesting => _engine;

    internal void OverrideEngine(IMidiEngine engine) => _engine = engine;

    private static IMidiEngine CreateEngine()
    {
        if (OperatingSystem.IsLinux())
            return new NullMidiEngine(); // placeholder until AlsaMidiEngine (Phase 3)

        // DryWetMidiEngine (Phase 1 follow-up) for Windows/macOS
        // For now, NullMidiEngine everywhere
        return new NullMidiEngine();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Zeus.Plugins.Midi/`
Expected: Succeeds.

- [ ] **Step 3: Commit**

```bash
git add Zeus.Plugins.Midi/ZeusMidiPlugin.cs
git commit -m "feat(midi): implement ZeusMidiPlugin entry point with device listing endpoint"
```

---

### Task 10: DryWetMidiEngine (Windows/macOS)

**Files:**
- Modify: `Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj` (add NuGet ref)
- Create: `Zeus.Plugins.Midi/Midi/DryWetMidiEngine.cs`
- Modify: `Zeus.Plugins.Midi/ZeusMidiPlugin.cs` (use DryWetMidiEngine)

- [ ] **Step 1: Add DryWetMidi NuGet reference**

```xml
<!-- Zeus.Plugins.Midi/Zeus.Plugins.Midi.csproj — add inside <Project> -->
<ItemGroup>
  <PackageReference Include="Melanchall.DryWetMidi" Version="8.0.3" />
</ItemGroup>
```

- [ ] **Step 2: Implement DryWetMidiEngine**

```csharp
// Zeus.Plugins.Midi/Midi/DryWetMidiEngine.cs
using Melanchall.DryWetMidi.Multimedia;
using Microsoft.Extensions.Logging;

namespace Zeus.Plugins.Midi.Midi;

public sealed class DryWetMidiEngine : IMidiEngine
{
    private readonly ILogger _log;
    private readonly Dictionary<string, InputDevice> _openDevices = new();
    private Timer? _pollTimer;
    private volatile bool _running;

    public DryWetMidiEngine(ILogger log) => _log = log;

    public event Action<MidiEvent>? EventReceived;
    public event Action<string>? DeviceConnected;
    public event Action<string>? DeviceDisconnected;

    public IReadOnlyList<MidiDeviceInfo> GetDevices()
    {
        try
        {
            return InputDevice.GetAll()
                .Select(d => new MidiDeviceInfo(d.Name, _openDevices.ContainsKey(d.Name)))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to enumerate MIDI devices");
            return [];
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        _running = true;
        PollDevices(null);
        _pollTimer = new Timer(PollDevices, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        _pollTimer?.Dispose();
        _pollTimer = null;

        foreach (var (name, device) in _openDevices)
        {
            try { device.StopEventsListening(); device.Dispose(); }
            catch (Exception ex) { _log.LogWarning(ex, "Error closing MIDI device {Name}", name); }
        }
        _openDevices.Clear();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _pollTimer?.Dispose();
        foreach (var device in _openDevices.Values)
        {
            try { device.Dispose(); } catch { }
        }
        _openDevices.Clear();
        return ValueTask.CompletedTask;
    }

    private void PollDevices(object? _)
    {
        if (!_running) return;

        try
        {
            var current = InputDevice.GetAll().Select(d => d.Name).ToHashSet();

            // Detect disconnected
            var removed = _openDevices.Keys.Where(k => !current.Contains(k)).ToList();
            foreach (var name in removed)
            {
                if (_openDevices.Remove(name, out var device))
                {
                    try { device.StopEventsListening(); device.Dispose(); }
                    catch (Exception ex) { _log.LogWarning(ex, "Error closing disconnected {Name}", name); }
                    DeviceDisconnected?.Invoke(name);
                    _log.LogInformation("MIDI device disconnected: {Name}", name);
                }
            }

            // Detect newly connected
            foreach (var name in current.Where(n => !_openDevices.ContainsKey(n)))
            {
                try
                {
                    var device = InputDevice.GetByName(name);
                    device.EventReceived += (sender, args) => OnMidiEventReceived(name, args);
                    device.StartEventsListening();
                    _openDevices[name] = device;
                    DeviceConnected?.Invoke(name);
                    _log.LogInformation("MIDI device connected: {Name}", name);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to open MIDI device {Name}", name);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MIDI device poll failed");
        }
    }

    private void OnMidiEventReceived(string deviceName, MidiEventReceivedEventArgs args)
    {
        var midiEvent = args.Event;
        MidiEvent? mapped = midiEvent switch
        {
            Melanchall.DryWetMidi.Core.ControlChangeEvent cc =>
                new MidiEvent(deviceName, MidiControlType.CC, cc.Channel, cc.ControlNumber, cc.ControlValue),
            Melanchall.DryWetMidi.Core.NoteOnEvent noteOn =>
                new MidiEvent(deviceName, MidiControlType.NoteOn, noteOn.Channel, noteOn.NoteNumber, noteOn.Velocity),
            Melanchall.DryWetMidi.Core.NoteOffEvent noteOff =>
                new MidiEvent(deviceName, MidiControlType.NoteOff, noteOff.Channel, noteOff.NoteNumber, noteOff.Velocity),
            Melanchall.DryWetMidi.Core.PitchBendEvent pb =>
                new MidiEvent(deviceName, MidiControlType.PitchBend, pb.Channel, 0, pb.PitchValue),
            _ => null
        };

        if (mapped is not null) EventReceived?.Invoke(mapped);
    }
}
```

- [ ] **Step 3: Update ZeusMidiPlugin.CreateEngine to use DryWetMidiEngine**

In `Zeus.Plugins.Midi/ZeusMidiPlugin.cs`, replace the `CreateEngine` method body:

```csharp
private IMidiEngine CreateEngine()
{
    if (OperatingSystem.IsLinux())
        return new NullMidiEngine(); // placeholder until AlsaMidiEngine (Phase 3)

    return new DryWetMidiEngine(_log);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build Zeus.Plugins.Midi/`
Expected: Succeeds. DryWetMidi NuGet package restores.

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Midi/
git commit -m "feat(midi): implement DryWetMidiEngine for Windows/macOS with hot-plug polling"
```

---

## Phase 2: Dispatch + Mapping

### Task 11: ZeusMidiCommand enum

**Files:**
- Create: `Zeus.Plugins.Midi/ZeusMidiCommand.cs`

- [ ] **Step 1: Define the full command enum**

```csharp
// Zeus.Plugins.Midi/ZeusMidiCommand.cs
namespace Zeus.Plugins.Midi;

public enum ZeusMidiCommand : byte
{
    // ── TX ────────────────────────────────────────────────
    Mox,
    Tune,
    TwoTone,
    PureSignal,

    // ── NR / NB ──────────────────────────────────────────
    Nr1,
    Nr2,
    Nr3,
    Nr4,
    Nb1,
    Nb2,
    Anf,
    Snb,

    // ── AGC ──────────────────────────────────────────────
    AutoAgc,
    AutoAtt,
    Preamp,

    // ── Mode selects ─────────────────────────────────────
    ModeNext,
    ModePrev,
    ModeLsb,
    ModeUsb,
    ModeCwl,
    ModeCwu,
    ModeAm,
    ModeFm,
    ModeSam,
    ModeDsb,
    ModeDigl,
    ModeDigu,

    // ── Filter ───────────────────────────────────────────
    FilterWider,
    FilterNarrower,

    // ── Zoom ─────────────────────────────────────────────
    ZoomIn,
    ZoomOut,

    // ── VFO buttons ──────────────────────────────────────
    VfoAUp100k,
    VfoADown100k,

    // ── Band ─────────────────────────────────────────────
    BandUp,
    BandDown,
    Band160m,
    Band80m,
    Band60m,
    Band40m,
    Band30m,
    Band20m,
    Band17m,
    Band15m,
    Band12m,
    Band10m,
    Band6m,
    Band2m,

    // ── Mute ─────────────────────────────────────────────
    Mute,

    // ── Knobs (absolute 0–127 → range) ──────────────────
    AfGain,
    AgcLevel,
    Drive,
    TuneDrive,
    MicGain,
    TxMonitor,
    Nr4Amount,
    Zoom,
    Attenuator,

    // ── Wheels (relative ±delta) ─────────────────────────
    VfoATune,
    VfoAMultiStep,
    FilterBandwidth,
    FilterHigh,
    FilterLow,
    FilterShift,
    TxFilterHigh,
    TxFilterLow,
    ZoomWheel,
    AfGainWheel,
    AgcLevelWheel,
    DriveWheel,

    // ── Meta ─────────────────────────────────────────────
    WheelSensUp,
    WheelSensDown,
    WheelSensToggle,
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Zeus.Plugins.Midi/`
Expected: Succeeds.

- [ ] **Step 3: Commit**

```bash
git add Zeus.Plugins.Midi/ZeusMidiCommand.cs
git commit -m "feat(midi): define ZeusMidiCommand enum (~69 commands)"
```

---

### Task 12: Mapping model and serialization

**Files:**
- Create: `Zeus.Plugins.Midi/Mapping/MidiMapping.cs`
- Create: `Zeus.Plugins.Midi/Mapping/MidiMappingSet.cs`
- Create: `Zeus.Plugins.Midi/Mapping/EncoderMode.cs`
- Create: `tests/Zeus.Plugins.Midi.Tests/MidiMappingSerializationTests.cs`

- [ ] **Step 1: Write the serialization test**

```csharp
// tests/Zeus.Plugins.Midi.Tests/MidiMappingSerializationTests.cs
using System.Text.Json;
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class MidiMappingSerializationTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var set = new MidiMappingSet
        {
            Version = 1,
            Devices =
            {
                ["Behringer CMD PL-1"] = new DeviceMappings
                {
                    Mappings =
                    [
                        new MidiMapping
                        {
                            ControlId = 16,
                            ControlType = MidiControlType.CC,
                            Channel = 0,
                            Command = ZeusMidiCommand.VfoATune,
                            Relative = true,
                            EncoderMode = EncoderMode.TwosComplement,
                            StepMultiplier = 1,
                        },
                        new MidiMapping
                        {
                            ControlId = 34,
                            ControlType = MidiControlType.NoteOn,
                            Channel = 0,
                            Command = ZeusMidiCommand.Mox,
                            Toggle = true,
                        },
                    ]
                }
            }
        };

        var json = JsonSerializer.Serialize(set);
        var deserialized = JsonSerializer.Deserialize<MidiMappingSet>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Version);
        Assert.True(deserialized.Devices.ContainsKey("Behringer CMD PL-1"));

        var mappings = deserialized.Devices["Behringer CMD PL-1"].Mappings;
        Assert.Equal(2, mappings.Count);
        Assert.Equal(ZeusMidiCommand.VfoATune, mappings[0].Command);
        Assert.True(mappings[0].Relative);
        Assert.Equal(EncoderMode.TwosComplement, mappings[0].EncoderMode);
        Assert.Equal(ZeusMidiCommand.Mox, mappings[1].Command);
        Assert.True(mappings[1].Toggle);
    }

    [Fact]
    public void Lookup_FindsMappingByKey()
    {
        var device = new DeviceMappings
        {
            Mappings =
            [
                new MidiMapping
                {
                    ControlId = 16,
                    ControlType = MidiControlType.CC,
                    Channel = 0,
                    Command = ZeusMidiCommand.VfoATune,
                },
            ]
        };

        var found = device.Find(MidiControlType.CC, 0, 16);
        Assert.NotNull(found);
        Assert.Equal(ZeusMidiCommand.VfoATune, found.Command);

        var notFound = device.Find(MidiControlType.CC, 0, 99);
        Assert.Null(notFound);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter MidiMappingSerialization`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement mapping types**

```csharp
// Zeus.Plugins.Midi/Mapping/EncoderMode.cs
namespace Zeus.Plugins.Midi.Mapping;

public enum EncoderMode : byte
{
    TwosComplement,   // 1–63 = CW, 65–127 = CCW (127 = -1)
    SignMagnitude,     // bit 6 = direction flag
    OffsetBinary,      // 64 = center, >64 CW, <64 CCW
}
```

```csharp
// Zeus.Plugins.Midi/Mapping/MidiMapping.cs
using System.Text.Json.Serialization;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Mapping;

public sealed class MidiMapping
{
    public int ControlId { get; set; }
    public MidiControlType ControlType { get; set; }
    public int Channel { get; set; }
    public ZeusMidiCommand Command { get; set; }

    // Options
    public bool Toggle { get; set; }
    public bool Relative { get; set; }
    public EncoderMode EncoderMode { get; set; }
    public int StepMultiplier { get; set; } = 1;
}
```

```csharp
// Zeus.Plugins.Midi/Mapping/MidiMappingSet.cs
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Mapping;

public sealed class MidiMappingSet
{
    public int Version { get; set; } = 1;
    public Dictionary<string, DeviceMappings> Devices { get; set; } = new();
}

public sealed class DeviceMappings
{
    public List<MidiMapping> Mappings { get; set; } = [];

    public MidiMapping? Find(MidiControlType controlType, int channel, int controlId)
        => Mappings.FirstOrDefault(m =>
            m.ControlType == controlType &&
            m.Channel == channel &&
            m.ControlId == controlId);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter MidiMappingSerialization`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Midi/Mapping/ tests/Zeus.Plugins.Midi.Tests/MidiMappingSerializationTests.cs
git commit -m "feat(midi): define mapping model with JSON serialization and device lookup"
```

---

### Task 13: ValueNormalizer

**Files:**
- Create: `Zeus.Plugins.Midi/Dispatch/ValueNormalizer.cs`
- Create: `tests/Zeus.Plugins.Midi.Tests/ValueNormalizerTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
// tests/Zeus.Plugins.Midi.Tests/ValueNormalizerTests.cs
using Zeus.Plugins.Midi.Dispatch;
using Zeus.Plugins.Midi.Mapping;

namespace Zeus.Plugins.Midi.Tests;

public class ValueNormalizerTests
{
    // ── Knob (absolute CC 0–127 → target range) ─────────────────

    [Theory]
    [InlineData(0, -50.0, 20.0, -50.0)]    // min → range min
    [InlineData(127, -50.0, 20.0, 20.0)]   // max → range max
    [InlineData(64, -50.0, 20.0, -14.724)] // midpoint (approx)
    [InlineData(0, 0.0, 100.0, 0.0)]       // 0–100 range
    [InlineData(127, 0.0, 100.0, 100.0)]
    public void ScaleKnob_MapsToRange(int value, double min, double max, double expected)
    {
        var result = ValueNormalizer.ScaleKnob(value, min, max);
        Assert.Equal(expected, result, precision: 0);
    }

    // ── Encoder (relative delta extraction) ─────────────────────

    [Theory]
    [InlineData(1, EncoderMode.TwosComplement, 1)]     // CW
    [InlineData(63, EncoderMode.TwosComplement, 63)]    // CW max
    [InlineData(127, EncoderMode.TwosComplement, -1)]   // CCW (127 = -1)
    [InlineData(65, EncoderMode.TwosComplement, -63)]   // CCW max
    [InlineData(64, EncoderMode.TwosComplement, 0)]     // center = no motion
    public void DecodeDelta_TwosComplement(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }

    [Theory]
    [InlineData(1, EncoderMode.SignMagnitude, 1)]       // CW
    [InlineData(63, EncoderMode.SignMagnitude, 63)]     // CW max
    [InlineData(65, EncoderMode.SignMagnitude, -1)]     // CCW (64 + magnitude)
    [InlineData(127, EncoderMode.SignMagnitude, -63)]   // CCW max
    public void DecodeDelta_SignMagnitude(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }

    [Theory]
    [InlineData(65, EncoderMode.OffsetBinary, 1)]      // > 64 = CW
    [InlineData(63, EncoderMode.OffsetBinary, -1)]     // < 64 = CCW
    [InlineData(64, EncoderMode.OffsetBinary, 0)]      // center = no motion
    public void DecodeDelta_OffsetBinary(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter ValueNormalizer`
Expected: FAIL

- [ ] **Step 3: Implement ValueNormalizer**

```csharp
// Zeus.Plugins.Midi/Dispatch/ValueNormalizer.cs
using Zeus.Plugins.Midi.Mapping;

namespace Zeus.Plugins.Midi.Dispatch;

public static class ValueNormalizer
{
    public static double ScaleKnob(int value, double min, double max)
    {
        var t = Math.Clamp(value, 0, 127) / 127.0;
        return min + t * (max - min);
    }

    public static int ScaleKnobInt(int value, int min, int max)
        => (int)Math.Round(ScaleKnob(value, min, max));

    public static int DecodeDelta(int rawValue, EncoderMode mode) => mode switch
    {
        EncoderMode.TwosComplement => rawValue switch
        {
            >= 1 and <= 63 => rawValue,
            64 => 0,
            >= 65 and <= 127 => rawValue - 128,
            _ => 0,
        },
        EncoderMode.SignMagnitude => rawValue switch
        {
            >= 1 and <= 63 => rawValue,
            >= 65 and <= 127 => -(rawValue - 64),
            _ => 0,
        },
        EncoderMode.OffsetBinary => rawValue - 64,
        _ => 0,
    };
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter ValueNormalizer`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Midi/Dispatch/ValueNormalizer.cs tests/Zeus.Plugins.Midi.Tests/ValueNormalizerTests.cs
git commit -m "feat(midi): implement value normalization — knob scaling and encoder delta decoding"
```

---

### Task 14: MidiDispatcher core

**Files:**
- Create: `Zeus.Plugins.Midi/Dispatch/MidiDispatcher.cs`
- Create: `tests/Zeus.Plugins.Midi.Tests/MidiDispatcherTests.cs`

- [ ] **Step 1: Write the dispatcher tests**

These tests use `NullMidiEngine` and a mock `IRadioCommandSurface`. Since `IRadioCommandSurface` is an interface, we can create a simple recording mock:

```csharp
// tests/Zeus.Plugins.Midi.Tests/MidiDispatcherTests.cs
using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Midi.Dispatch;
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class MidiDispatcherTests
{
    private readonly RecordingSurface _surface = new();
    private readonly MidiDispatcher _dispatcher;

    public MidiDispatcherTests()
    {
        _dispatcher = new MidiDispatcher(_surface);
    }

    [Fact]
    public void VfoATune_CC_DispatchesSetVfo()
    {
        var mapping = MakeMapping(MidiControlType.CC, 16, ZeusMidiCommand.VfoATune,
            relative: true, encoderMode: EncoderMode.TwosComplement);
        _dispatcher.SetMapping("TestDevice", mapping);

        _dispatcher.HandleEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 16, 1));

        Assert.Single(_surface.Calls);
        Assert.Equal("SetVfo", _surface.Calls[0].Method);
    }

    [Fact]
    public void Mox_NoteOn_TogglesMox()
    {
        var mapping = MakeMapping(MidiControlType.NoteOn, 34, ZeusMidiCommand.Mox, toggle: true);
        _dispatcher.SetMapping("TestDevice", mapping);

        _dispatcher.HandleEvent(new MidiEvent("TestDevice", MidiControlType.NoteOn, 0, 34, 127));

        Assert.Single(_surface.Calls);
        Assert.Equal("TrySetMox", _surface.Calls[0].Method);
        Assert.Equal(true, _surface.Calls[0].Args[0]); // on=true (was off)
    }

    [Fact]
    public void Drive_Knob_ScalesTo0_100()
    {
        var mapping = MakeMapping(MidiControlType.CC, 1, ZeusMidiCommand.Drive);
        _dispatcher.SetMapping("TestDevice", mapping);

        _dispatcher.HandleEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 1, 64));

        Assert.Single(_surface.Calls);
        Assert.Equal("SetDrive", _surface.Calls[0].Method);
        Assert.Equal(50, _surface.Calls[0].Args[0]); // 64/127 * 100 ≈ 50
    }

    [Fact]
    public void UnmappedEvent_IsIgnored()
    {
        _dispatcher.HandleEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 99, 64));
        Assert.Empty(_surface.Calls);
    }

    private static MidiMapping MakeMapping(
        MidiControlType type, int controlId, ZeusMidiCommand cmd,
        bool toggle = false, bool relative = false,
        EncoderMode encoderMode = EncoderMode.TwosComplement)
        => new()
        {
            ControlType = type,
            Channel = 0,
            ControlId = controlId,
            Command = cmd,
            Toggle = toggle,
            Relative = relative,
            EncoderMode = encoderMode,
            StepMultiplier = 1,
        };
}

/// <summary>
/// Records IRadioCommandSurface calls for assertion.
/// Returns a default StateDto from every method that needs one.
/// StateDto's first 7 params are required — use this shared default.
/// </summary>
internal sealed class RecordingSurface : IRadioCommandSurface
{
    public record Call(string Method, object?[] Args);
    public List<Call> Calls { get; } = [];

    private static readonly StateDto Default = new(
        Status: ConnectionStatus.Disconnected,
        Endpoint: null,
        VfoHz: 14_200_000,
        Mode: RxMode.USB,
        FilterLowHz: 150,
        FilterHighHz: 2850,
        SampleRate: 48000);

    private StateDto Record(string method, params object?[] args)
    {
        Calls.Add(new Call(method, args));
        return Default;
    }

    public StateDto Snapshot() => Default;
    public StateDto SetVfo(long hz) => Record("SetVfo", hz);
    public StateDto SetMode(RxMode mode) => Record("SetMode", mode);
    public StateDto SetFilter(int lo, int hi) => Record("SetFilter", lo, hi);
    public StateDto SetTxFilter(int lo, int hi) => Record("SetTxFilter", lo, hi);
    public bool TrySetMox(bool on, out string? error) { Calls.Add(new Call("TrySetMox", [on])); error = null; return true; }
    public bool TrySetTun(bool on, out string? error) { Calls.Add(new Call("TrySetTun", [on])); error = null; return true; }
    public void SetDrive(int pct) => Calls.Add(new Call("SetDrive", [pct]));
    public void SetTuneDrive(int pct) => Calls.Add(new Call("SetTuneDrive", [pct]));
    public StateDto SetRxAfGain(double db) => Record("SetRxAfGain", db);
    public StateDto SetAgcTop(double db) => Record("SetAgcTop", db);
    public StateDto SetAutoAgc(bool on) => Record("SetAutoAgc", on);
    public StateDto SetPreamp(bool on) => Record("SetPreamp", on);
    public StateDto SetAttenuator(int db) => Record("SetAttenuator", db);
    public StateDto SetAutoAtt(bool on) => Record("SetAutoAtt", on);
    public StateDto SetNr(NrConfig cfg) => Record("SetNr", cfg);
    public StateDto SetNr4(Nr4ConfigSetRequest req) => Record("SetNr4", req);
    public StateDto SetZoom(int level) => Record("SetZoom", level);
    public StateDto SetTxMonitor(TxMonitorSetRequest req) => Record("SetTxMonitor", req);
    public StateDto SetTwoTone(TwoToneSetRequest req) => Record("SetTwoTone", req);
    public StateDto SetPs(PsControlSetRequest req) => Record("SetPs", req);
    public StateDto SetTxMicGain(int db) => Record("SetTxMicGain", db);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter MidiDispatcher`
Expected: FAIL — `MidiDispatcher` doesn't exist.

- [ ] **Step 3: Implement MidiDispatcher**

```csharp
// Zeus.Plugins.Midi/Dispatch/MidiDispatcher.cs
using System.Collections.Concurrent;
using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Dispatch;

public sealed class MidiDispatcher
{
    private readonly IRadioCommandSurface _surface;
    private readonly ConcurrentDictionary<string, DeviceMappings> _mappings = new();

    public MidiDispatcher(IRadioCommandSurface surface) => _surface = surface;

    public void SetMapping(string deviceName, MidiMapping mapping)
    {
        var device = _mappings.GetOrAdd(deviceName, _ => new DeviceMappings());
        device.Mappings.RemoveAll(m =>
            m.ControlType == mapping.ControlType &&
            m.Channel == mapping.Channel &&
            m.ControlId == mapping.ControlId);
        device.Mappings.Add(mapping);
    }

    public void LoadMappings(MidiMappingSet set)
    {
        _mappings.Clear();
        foreach (var (name, device) in set.Devices)
            _mappings[name] = device;
    }

    public void HandleEvent(MidiEvent ev)
    {
        if (!_mappings.TryGetValue(ev.DeviceName, out var device)) return;
        var mapping = device.Find(ev.ControlType, ev.Channel, ev.ControlId);
        if (mapping is null) return;

        Dispatch(mapping, ev.Value);
    }

    private void Dispatch(MidiMapping mapping, int value)
    {
        var cmd = mapping.Command;

        // ── Buttons (toggle/momentary) ──────────────────────
        if (IsButton(cmd))
        {
            DispatchButton(cmd, mapping);
            return;
        }

        // ── Knobs (absolute 0–127) ──────────────────────────
        if (IsKnob(cmd))
        {
            DispatchKnob(cmd, value);
            return;
        }

        // ── Wheels (relative encoder) ───────────────────────
        if (IsWheel(cmd))
        {
            var delta = ValueNormalizer.DecodeDelta(value, mapping.EncoderMode);
            delta *= mapping.StepMultiplier;
            if (delta == 0) return;
            DispatchWheel(cmd, delta);
            return;
        }
    }

    private void DispatchButton(ZeusMidiCommand cmd, MidiMapping mapping)
    {
        var state = _surface.Snapshot();

        switch (cmd)
        {
            case ZeusMidiCommand.Mox:
                _surface.TrySetMox(!state.Status.HasFlag(ConnectionStatus.Connected) || mapping.Toggle, out _);
                break;
            case ZeusMidiCommand.Tune:
                _surface.TrySetTun(true, out _);
                break;
            case ZeusMidiCommand.TwoTone:
                _surface.SetTwoTone(new TwoToneSetRequest(!state.TwoToneEnabled));
                break;
            case ZeusMidiCommand.PureSignal:
                _surface.SetPs(new PsControlSetRequest(!state.PsEnabled, state.PsAuto, state.PsSingle));
                break;

            case ZeusMidiCommand.Nr1:
                ToggleNrMode(state, NrMode.Anr);
                break;
            case ZeusMidiCommand.Nr2:
                ToggleNrMode(state, NrMode.Emnr);
                break;
            case ZeusMidiCommand.Nr3:
                ToggleNrMode(state, NrMode.Sbnr);
                break;
            case ZeusMidiCommand.Nr4:
                _surface.SetNr4(new Nr4ConfigSetRequest());
                break;
            case ZeusMidiCommand.Nb1:
                ToggleNbMode(state, NbMode.Nb1);
                break;
            case ZeusMidiCommand.Nb2:
                ToggleNbMode(state, NbMode.Nb2);
                break;
            case ZeusMidiCommand.Anf:
                var nrAnf = state.Nr ?? new NrConfig();
                _surface.SetNr(nrAnf with { AnfEnabled = !nrAnf.AnfEnabled });
                break;
            case ZeusMidiCommand.Snb:
                var nrSnb = state.Nr ?? new NrConfig();
                _surface.SetNr(nrSnb with { SnbEnabled = !nrSnb.SnbEnabled });
                break;

            case ZeusMidiCommand.AutoAgc:
                _surface.SetAutoAgc(!state.AutoAgcEnabled);
                break;
            case ZeusMidiCommand.AutoAtt:
                _surface.SetAutoAtt(!state.AutoAttEnabled);
                break;
            case ZeusMidiCommand.Preamp:
                _surface.SetPreamp(!state.Status.HasFlag(ConnectionStatus.Connected)); // TODO: read actual preamp state
                break;

            case ZeusMidiCommand.ModeNext:
                _surface.SetMode(NextMode(state.Mode, +1));
                break;
            case ZeusMidiCommand.ModePrev:
                _surface.SetMode(NextMode(state.Mode, -1));
                break;

            case >= ZeusMidiCommand.ModeLsb and <= ZeusMidiCommand.ModeDigu:
                _surface.SetMode((RxMode)(cmd - ZeusMidiCommand.ModeLsb));
                break;

            case ZeusMidiCommand.FilterWider:
                _surface.SetFilter(state.FilterLowHz - 50, state.FilterHighHz + 50);
                break;
            case ZeusMidiCommand.FilterNarrower:
                _surface.SetFilter(state.FilterLowHz + 50, state.FilterHighHz - 50);
                break;

            case ZeusMidiCommand.ZoomIn:
                _surface.SetZoom(state.ZoomLevel + 1);
                break;
            case ZeusMidiCommand.ZoomOut:
                _surface.SetZoom(Math.Max(1, state.ZoomLevel - 1));
                break;

            case ZeusMidiCommand.VfoAUp100k:
                _surface.SetVfo(state.VfoHz + 100_000);
                break;
            case ZeusMidiCommand.VfoADown100k:
                _surface.SetVfo(state.VfoHz - 100_000);
                break;
        }
    }

    private void DispatchKnob(ZeusMidiCommand cmd, int value)
    {
        switch (cmd)
        {
            case ZeusMidiCommand.AfGain:
                _surface.SetRxAfGain(ValueNormalizer.ScaleKnob(value, -50, 20));
                break;
            case ZeusMidiCommand.AgcLevel:
                _surface.SetAgcTop(ValueNormalizer.ScaleKnob(value, -20, 120));
                break;
            case ZeusMidiCommand.Drive:
                _surface.SetDrive(ValueNormalizer.ScaleKnobInt(value, 0, 100));
                break;
            case ZeusMidiCommand.TuneDrive:
                _surface.SetTuneDrive(ValueNormalizer.ScaleKnobInt(value, 0, 100));
                break;
            case ZeusMidiCommand.MicGain:
                _surface.SetTxMicGain(ValueNormalizer.ScaleKnobInt(value, -40, 10));
                break;
            case ZeusMidiCommand.TxMonitor:
                _surface.SetTxMonitor(new TxMonitorSetRequest(value > 0));
                break;
            case ZeusMidiCommand.Nr4Amount:
                _surface.SetNr4(new Nr4ConfigSetRequest(ReductionAmount: ValueNormalizer.ScaleKnob(value, 0, 100)));
                break;
            case ZeusMidiCommand.Zoom:
                _surface.SetZoom(ValueNormalizer.ScaleKnobInt(value, 1, 48));
                break;
            case ZeusMidiCommand.Attenuator:
                _surface.SetAttenuator(ValueNormalizer.ScaleKnobInt(value, 0, 31));
                break;
        }
    }

    private void DispatchWheel(ZeusMidiCommand cmd, int delta)
    {
        var state = _surface.Snapshot();

        switch (cmd)
        {
            case ZeusMidiCommand.VfoATune:
                _surface.SetVfo(state.VfoHz + delta * 10); // 10 Hz per click default
                break;
            case ZeusMidiCommand.VfoAMultiStep:
                _surface.SetVfo(state.VfoHz + delta * 1000);
                break;
            case ZeusMidiCommand.FilterBandwidth:
                _surface.SetFilter(state.FilterLowHz - delta * 25, state.FilterHighHz + delta * 25);
                break;
            case ZeusMidiCommand.FilterHigh:
                _surface.SetFilter(state.FilterLowHz, state.FilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.FilterLow:
                _surface.SetFilter(state.FilterLowHz + delta * 10, state.FilterHighHz);
                break;
            case ZeusMidiCommand.FilterShift:
                _surface.SetFilter(state.FilterLowHz + delta * 10, state.FilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.TxFilterHigh:
                _surface.SetTxFilter(state.TxFilterLowHz, state.TxFilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.TxFilterLow:
                _surface.SetTxFilter(state.TxFilterLowHz + delta * 10, state.TxFilterHighHz);
                break;
            case ZeusMidiCommand.ZoomWheel:
                _surface.SetZoom(Math.Max(1, state.ZoomLevel + delta));
                break;
            case ZeusMidiCommand.AfGainWheel:
                _surface.SetRxAfGain(state.RxAfGainDb + delta * 0.5);
                break;
            case ZeusMidiCommand.AgcLevelWheel:
                _surface.SetAgcTop(state.AgcTopDb + delta);
                break;
            case ZeusMidiCommand.DriveWheel:
                _surface.SetDrive(Math.Clamp(state.DrivePct + delta, 0, 100));
                break;
        }
    }

    private void ToggleNrMode(StateDto state, NrMode target)
    {
        var nr = state.Nr ?? new NrConfig();
        _surface.SetNr(nr with { NrMode = nr.NrMode == target ? NrMode.Off : target });
    }

    private void ToggleNbMode(StateDto state, NbMode target)
    {
        var nr = state.Nr ?? new NrConfig();
        _surface.SetNr(nr with { NbMode = nr.NbMode == target ? NbMode.Off : target });
    }

    private static RxMode NextMode(RxMode current, int direction)
    {
        var values = Enum.GetValues<RxMode>();
        var index = Array.IndexOf(values, current);
        index = (index + direction + values.Length) % values.Length;
        return values[index];
    }

    private static bool IsButton(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.Mox and <= ZeusMidiCommand.VfoADown100k
            or >= ZeusMidiCommand.BandUp and <= ZeusMidiCommand.Mute;

    private static bool IsKnob(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.AfGain and <= ZeusMidiCommand.Attenuator;

    private static bool IsWheel(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.VfoATune and <= ZeusMidiCommand.DriveWheel;
}
```

**Important:** the `IsButton`/`IsKnob`/`IsWheel` range checks depend on the enum ordering from Task 11. Verify the ranges match. If the enum values shift, adjust the range boundaries.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter MidiDispatcher`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Zeus.Plugins.Midi/Dispatch/MidiDispatcher.cs tests/Zeus.Plugins.Midi.Tests/MidiDispatcherTests.cs
git commit -m "feat(midi): implement MidiDispatcher — MIDI events to IRadioCommandSurface calls"
```

---

### Task 15: Wire dispatcher into plugin and add mapping REST endpoints

**Files:**
- Modify: `Zeus.Plugins.Midi/ZeusMidiPlugin.cs`

- [ ] **Step 1: Integrate dispatcher and mapping endpoints into the plugin**

Update `ZeusMidiPlugin.cs` to create a `MidiDispatcher`, wire it to the engine's `EventReceived`, and add mapping CRUD REST endpoints:

```csharp
// Zeus.Plugins.Midi/ZeusMidiPlugin.cs — updated version
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Midi.Dispatch;
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi;

public sealed class ZeusMidiPlugin : IZeusPlugin, IBackendPlugin
{
    private const string MappingsKey = "mappings";

    private ILogger _log = null!;
    private IRadioCommandSurface? _surface;
    private IPluginSettings _settings = null!;
    private IMidiEngine _engine = null!;
    private MidiDispatcher? _dispatcher;

    public async Task InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _log = context.Logger;
        _surface = context.CommandSurface;
        _settings = context.Settings;
        _engine = CreateEngine();

        if (_surface is not null)
        {
            _dispatcher = new MidiDispatcher(_surface);
            _engine.EventReceived += _dispatcher.HandleEvent;

            var saved = await _settings.GetAsync<MidiMappingSet>(MappingsKey, ct)
                .ConfigureAwait(false);
            if (saved is not null) _dispatcher.LoadMappings(saved);
        }

        await _engine.StartAsync(ct).ConfigureAwait(false);
        _log.LogInformation("MIDI plugin initialized ({DeviceCount} devices)",
            _engine.GetDevices().Count);
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        if (_dispatcher is not null)
            _engine.EventReceived -= _dispatcher.HandleEvent;

        await _engine.StopAsync(ct).ConfigureAwait(false);
        await _engine.DisposeAsync().ConfigureAwait(false);
        _log.LogInformation("MIDI plugin shut down");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("devices", () =>
            Results.Ok(_engine.GetDevices()));

        endpoints.MapGet("mappings", async (CancellationToken ct) =>
        {
            var set = await _settings.GetAsync<MidiMappingSet>(MappingsKey, ct);
            return Results.Ok(set ?? new MidiMappingSet());
        });

        endpoints.MapPut("mappings", async (MidiMappingSet set, CancellationToken ct) =>
        {
            await _settings.SetAsync(MappingsKey, set, ct);
            _dispatcher?.LoadMappings(set);
            return Results.NoContent();
        });

        endpoints.MapGet("commands", () =>
            Results.Ok(Enum.GetNames<ZeusMidiCommand>()));
    }

    internal IMidiEngine EngineForTesting => _engine;
    internal void OverrideEngine(IMidiEngine engine) => _engine = engine;

    private IMidiEngine CreateEngine()
    {
        if (OperatingSystem.IsLinux())
            return new NullMidiEngine();

        return new DryWetMidiEngine(_log);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Zeus.Plugins.Midi/`
Expected: Succeeds.

- [ ] **Step 3: Commit**

```bash
git add Zeus.Plugins.Midi/ZeusMidiPlugin.cs
git commit -m "feat(midi): wire dispatcher into plugin, add mapping CRUD + command list endpoints"
```

---

## Phase 3: Linux ALSA (Outline)

### Task 16: AlsaMidiEngine (outline — requires Linux environment)

**Files:**
- Create: `Zeus.Plugins.Midi/Midi/AlsaMidiEngine.cs`

This task requires a Linux environment with `libasound.so` for testing. The implementation is a thin P/Invoke shim (~200 lines) using:
- `snd_seq_open` / `snd_seq_close` — lifecycle
- `snd_seq_create_simple_port` — create input port
- `snd_seq_client_info_*` / `snd_seq_port_info_*` — enumerate
- `snd_seq_connect_from` — subscribe
- `snd_seq_event_input` — blocking read

The engine follows the same `IMidiEngine` contract. Hot-plug via 2-second polling of client/port enumeration. A dedicated reader thread calls `snd_seq_event_input` in a loop and translates ALSA events to `MidiEvent`.

- [ ] **Step 1:** Implement `AlsaMidiEngine` following the ALSA seq API described in §5.2 of the PRD
- [ ] **Step 2:** Update `ZeusMidiPlugin.CreateEngine()` to return `AlsaMidiEngine` on Linux
- [ ] **Step 3:** Manual test on a Linux machine with a USB MIDI controller
- [ ] **Step 4:** Commit

---

## Phase 4: UI + Learn Mode

### Task 17: Learn mode backend

**Files:**
- Create: `Zeus.Plugins.Midi/Learn/LearnSession.cs`
- Create: `tests/Zeus.Plugins.Midi.Tests/LearnSessionTests.cs`

- [ ] **Step 1: Write the test**

```csharp
// tests/Zeus.Plugins.Midi.Tests/LearnSessionTests.cs
using Zeus.Plugins.Midi.Learn;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class LearnSessionTests
{
    [Fact]
    public void Start_CapturesNextEvent()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.VfoATune);

        Assert.True(session.IsActive);
        Assert.Null(session.LastCaptured);

        session.OfferEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 16, 65));

        Assert.NotNull(session.LastCaptured);
        Assert.Equal(MidiControlType.CC, session.LastCaptured.ControlType);
        Assert.Equal(16, session.LastCaptured.ControlId);
    }

    [Fact]
    public void Start_IgnoresEventsFromOtherDevices()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.VfoATune);

        session.OfferEvent(new MidiEvent("OtherDevice", MidiControlType.CC, 0, 1, 64));
        Assert.Null(session.LastCaptured);
    }

    [Fact]
    public void Stop_ReturnsCapture()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.Drive);
        session.OfferEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 7, 100));

        var result = session.Stop();
        Assert.NotNull(result);
        Assert.Equal(7, result.ControlId);
        Assert.Equal(ZeusMidiCommand.Drive, result.Command);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void Stop_WhenNothingCaptured_ReturnsNull()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.Mox);
        var result = session.Stop();
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter LearnSession`
Expected: FAIL

- [ ] **Step 3: Implement LearnSession**

```csharp
// Zeus.Plugins.Midi/Learn/LearnSession.cs
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Learn;

public sealed class LearnSession
{
    private string? _deviceName;
    private ZeusMidiCommand _command;

    public bool IsActive => _deviceName is not null;
    public MidiEvent? LastCaptured { get; private set; }

    public void Start(string deviceName, ZeusMidiCommand command)
    {
        _deviceName = deviceName;
        _command = command;
        LastCaptured = null;
    }

    public void OfferEvent(MidiEvent ev)
    {
        if (!IsActive) return;
        if (!string.Equals(ev.DeviceName, _deviceName, StringComparison.OrdinalIgnoreCase)) return;
        LastCaptured = ev;
    }

    public LearnResult? Stop()
    {
        var result = LastCaptured is not null
            ? new LearnResult(_command, LastCaptured.ControlType, LastCaptured.Channel, LastCaptured.ControlId)
            : null;

        _deviceName = null;
        LastCaptured = null;
        return result;
    }
}

public sealed record LearnResult(
    ZeusMidiCommand Command,
    MidiControlType ControlType,
    int Channel,
    int ControlId);
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Zeus.Plugins.Midi.Tests/ --filter LearnSession`
Expected: PASS (4 tests)

- [ ] **Step 5: Wire learn endpoints into ZeusMidiPlugin.MapEndpoints**

Add inside `MapEndpoints`:

```csharp
var learn = new LearnSession();
_engine.EventReceived += ev => learn.OfferEvent(ev);

endpoints.MapPost("learn/start", (LearnStartRequest req) =>
{
    learn.Start(req.DeviceName, req.Command);
    return Results.NoContent();
});

endpoints.MapGet("learn/last", () =>
    learn.LastCaptured is { } ev
        ? Results.Ok(new { ev.ControlType, ev.Channel, ev.ControlId, ev.Value })
        : Results.Ok<object?>(null));

endpoints.MapPost("learn/stop", () =>
{
    var result = learn.Stop();
    return result is not null ? Results.Ok(result) : Results.Ok<object?>(null);
});
```

Add the request record:

```csharp
// inside ZeusMidiPlugin.cs or a separate file
public sealed record LearnStartRequest(string DeviceName, ZeusMidiCommand Command);
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build Zeus.slnx && dotnet test tests/Zeus.Plugins.Midi.Tests/`
Expected: All pass.

- [ ] **Step 7: Commit**

```bash
git add Zeus.Plugins.Midi/Learn/ tests/Zeus.Plugins.Midi.Tests/LearnSessionTests.cs Zeus.Plugins.Midi/ZeusMidiPlugin.cs
git commit -m "feat(midi): implement learn mode backend with capture session and REST endpoints"
```

---

### Task 18: React settings panel (outline)

**Files:**
- Create: `Zeus.Plugins.Midi/ui/midi-settings.tsx`

The settings panel is a React ESM module mounted at `settings.plugins` slot. It:
1. Fetches `/api/plugins/com.openhpsdr.zeus.midi/devices` to list connected controllers
2. Fetches `/api/plugins/com.openhpsdr.zeus.midi/mappings` to display current mappings
3. Fetches `/api/plugins/com.openhpsdr.zeus.midi/commands` for the command dropdown
4. Implements learn mode: POST `learn/start`, poll `learn/last` at 200ms, POST `learn/stop`
5. Saves via PUT `mappings`

Layout follows the wireframe in PRD §9.1. Use Zeus design tokens from `zeus-web/src/styles/tokens.css`. Build with Vite into `midi-settings.es.js`.

- [ ] **Step 1:** Create the React component following the existing plugin UI pattern from the plugin author guide (§4)
- [ ] **Step 2:** Wire the learn mode flow (start → poll → stop → save)
- [ ] **Step 3:** Build the ESM module
- [ ] **Step 4:** Test manually in the browser with a connected MIDI device
- [ ] **Step 5:** Commit

---

## Phase 5: Polish (Outline)

### Task 19: CC flood throttle

**Files:**
- Create: `Zeus.Plugins.Midi/Dispatch/CcThrottle.cs`
- Test: `tests/Zeus.Plugins.Midi.Tests/CcThrottleTests.cs`

Coalesce rapid CC values per-control to max 30 Hz dispatch rate. Use a timer-based debounce: on CC arrival, store the latest value; on timer tick (33ms), dispatch if changed.

- [ ] **Step 1:** Write failing test — 100 events in 10ms, assert ≤3 dispatches
- [ ] **Step 2:** Implement `CcThrottle`
- [ ] **Step 3:** Integrate into `MidiDispatcher.HandleEvent`
- [ ] **Step 4:** Run tests, commit

### Task 20: Encoder mode auto-detection heuristic

After receiving N events (e.g. 10), analyze the value distribution to guess whether the encoder is twos-complement, sign-magnitude, or offset-binary. Log the guess and apply it. This is a nice-to-have and can be a simple heuristic (e.g. if most values cluster around 64, it's offset-binary).

### Task 21: Wheel sensitivity (messages-per-step)

The `WheelSensUp`/`WheelSensDown`/`WheelSensToggle` meta-commands adjust a `_messagesPerStep` counter in `MidiDispatcher`. VFO tune commands accumulate encoder deltas and only dispatch `SetVfo` when the accumulated delta reaches the threshold.

---

## Cross-Cutting Concerns

### Build Verification

After every phase, verify:
```bash
dotnet build Zeus.slnx
dotnet test Zeus.slnx
```

### Files NOT to modify

Per CLAUDE.md red-light rules:
- Do NOT modify `zeus-web/src/styles/tokens.css` (visual design)
- Do NOT change defaults an operator would notice (drive, filter, AGC)
- Do NOT add the plugin to `.claude/settings.json` in upstream PRs

### Key References (read before implementing)

- Plugin author guide: `docs/plugins/author-guide.md`
- Plugin manifest spec: `docs/plugins/manifest-spec.md`
- Plugin capabilities: `docs/plugins/capabilities.md`
- Existing plugin tests: `tests/Zeus.Plugins.Host.Tests/EndToEndPluginTests.cs`
- RadioService API: `Zeus.Server.Hosting/RadioService.cs`
- TxService API: `Zeus.Server.Hosting/TxService.cs`
- TciSession dispatch pattern: `Zeus.Server.Hosting/Tci/TciSession.cs`
- StateDto: `Zeus.Contracts/Dtos.cs:121-292`
- NrMode/NbMode enums: `Zeus.Contracts/Dtos.cs:68,73`
- MoxSource: `Zeus.Contracts/MoxSource.cs`
- HpsdrAtten: `Zeus.Protocol1/HpsdrEnums.cs:73-81`
