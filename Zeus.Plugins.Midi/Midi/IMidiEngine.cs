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
