using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Mapping;

public sealed class MidiMapping
{
    public int ControlId { get; set; }
    public MidiControlType ControlType { get; set; }
    public int Channel { get; set; }
    public ZeusMidiCommand Command { get; set; }
    public bool Toggle { get; set; }
    public bool Relative { get; set; }
    public EncoderMode EncoderMode { get; set; }
    public int StepMultiplier { get; set; } = 1;
}
