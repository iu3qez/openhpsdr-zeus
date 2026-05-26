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
