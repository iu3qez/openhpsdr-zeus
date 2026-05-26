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

    private IMidiEngine CreateEngine()
    {
        if (OperatingSystem.IsLinux())
            return new NullMidiEngine();

        return new DryWetMidiEngine(_log);
    }
}
