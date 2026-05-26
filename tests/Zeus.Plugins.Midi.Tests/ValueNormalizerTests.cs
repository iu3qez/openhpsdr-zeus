using Zeus.Plugins.Midi.Dispatch;
using Zeus.Plugins.Midi.Mapping;

namespace Zeus.Plugins.Midi.Tests;

public class ValueNormalizerTests
{
    [Theory]
    [InlineData(0, -50.0, 20.0, -50.0)]
    [InlineData(127, -50.0, 20.0, 20.0)]
    [InlineData(0, 0.0, 100.0, 0.0)]
    [InlineData(127, 0.0, 100.0, 100.0)]
    public void ScaleKnob_MapsToRange(int value, double min, double max, double expected)
    {
        var result = ValueNormalizer.ScaleKnob(value, min, max);
        Assert.Equal(expected, result, precision: 0);
    }

    [Theory]
    [InlineData(1, EncoderMode.TwosComplement, 1)]
    [InlineData(63, EncoderMode.TwosComplement, 63)]
    [InlineData(127, EncoderMode.TwosComplement, -1)]
    [InlineData(65, EncoderMode.TwosComplement, -63)]
    [InlineData(64, EncoderMode.TwosComplement, 0)]
    public void DecodeDelta_TwosComplement(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }

    [Theory]
    [InlineData(1, EncoderMode.SignMagnitude, 1)]
    [InlineData(63, EncoderMode.SignMagnitude, 63)]
    [InlineData(65, EncoderMode.SignMagnitude, -1)]
    [InlineData(127, EncoderMode.SignMagnitude, -63)]
    public void DecodeDelta_SignMagnitude(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }

    [Theory]
    [InlineData(65, EncoderMode.OffsetBinary, 1)]
    [InlineData(63, EncoderMode.OffsetBinary, -1)]
    [InlineData(64, EncoderMode.OffsetBinary, 0)]
    public void DecodeDelta_OffsetBinary(int rawValue, EncoderMode mode, int expected)
    {
        Assert.Equal(expected, ValueNormalizer.DecodeDelta(rawValue, mode));
    }
}
