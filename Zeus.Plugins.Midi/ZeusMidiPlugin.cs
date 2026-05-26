using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Extensions;
using Zeus.Plugins.Midi.Dispatch;
using Zeus.Plugins.Midi.Learn;
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

        var learn = new LearnSession();
        _engine.EventReceived += learn.OfferEvent;

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

public sealed record LearnStartRequest(string DeviceName, ZeusMidiCommand Command);
