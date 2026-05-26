namespace Zeus.Plugins.Midi.Midi;

public sealed record MidiEvent(
    string DeviceName,
    MidiControlType ControlType,
    int Channel,
    int ControlId,
    int Value);
