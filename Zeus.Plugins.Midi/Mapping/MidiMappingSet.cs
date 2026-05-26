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
