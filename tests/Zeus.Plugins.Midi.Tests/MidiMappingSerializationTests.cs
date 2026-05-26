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
