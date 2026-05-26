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
