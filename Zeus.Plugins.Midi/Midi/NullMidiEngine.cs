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
