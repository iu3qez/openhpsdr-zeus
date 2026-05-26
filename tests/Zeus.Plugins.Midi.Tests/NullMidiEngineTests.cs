using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class NullMidiEngineTests
{
    [Fact]
    public void GetDevices_ReturnsEmpty()
    {
        using var engine = new NullMidiEngine();
        Assert.Empty(engine.GetDevices());
    }

    [Fact]
    public async Task InjectEvent_RaisesEventReceived()
    {
        await using var engine = new NullMidiEngine();
        await engine.StartAsync(CancellationToken.None);

        MidiEvent? received = null;
        engine.EventReceived += e => received = e;

        var ev = new MidiEvent("TestDevice", MidiControlType.CC, 0, 16, 65);
        engine.InjectEvent(ev);

        Assert.NotNull(received);
        Assert.Equal("TestDevice", received.DeviceName);
        Assert.Equal(MidiControlType.CC, received.ControlType);
        Assert.Equal(16, received.ControlId);
        Assert.Equal(65, received.Value);
    }

    [Fact]
    public async Task InjectEvent_WhenStopped_DoesNotRaise()
    {
        await using var engine = new NullMidiEngine();
        MidiEvent? received = null;
        engine.EventReceived += e => received = e;

        engine.InjectEvent(new MidiEvent("Test", MidiControlType.CC, 0, 1, 64));
        Assert.Null(received);
    }
}
