using Zeus.Plugins.Midi.Learn;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Tests;

public class LearnSessionTests
{
    [Fact]
    public void Start_CapturesNextEvent()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.VfoATune);

        Assert.True(session.IsActive);
        Assert.Null(session.LastCaptured);

        session.OfferEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 16, 65));

        Assert.NotNull(session.LastCaptured);
        Assert.Equal(MidiControlType.CC, session.LastCaptured.ControlType);
        Assert.Equal(16, session.LastCaptured.ControlId);
    }

    [Fact]
    public void Start_IgnoresEventsFromOtherDevices()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.VfoATune);

        session.OfferEvent(new MidiEvent("OtherDevice", MidiControlType.CC, 0, 1, 64));
        Assert.Null(session.LastCaptured);
    }

    [Fact]
    public void Stop_ReturnsCapture()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.Drive);
        session.OfferEvent(new MidiEvent("TestDevice", MidiControlType.CC, 0, 7, 100));

        var result = session.Stop();
        Assert.NotNull(result);
        Assert.Equal(7, result.ControlId);
        Assert.Equal(ZeusMidiCommand.Drive, result.Command);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void Stop_WhenNothingCaptured_ReturnsNull()
    {
        var session = new LearnSession();
        session.Start("TestDevice", ZeusMidiCommand.Mox);
        var result = session.Stop();
        Assert.Null(result);
    }
}
