using Zeus.Plugins.Midi.Mapping;

namespace Zeus.Plugins.Midi.Dispatch;

public static class ValueNormalizer
{
    public static double ScaleKnob(int value, double min, double max)
    {
        var t = Math.Clamp(value, 0, 127) / 127.0;
        return min + t * (max - min);
    }

    public static int ScaleKnobInt(int value, int min, int max)
        => (int)Math.Round(ScaleKnob(value, min, max));

    public static int DecodeDelta(int rawValue, EncoderMode mode) => mode switch
    {
        EncoderMode.TwosComplement => rawValue switch
        {
            >= 1 and <= 63 => rawValue,
            64 => 0,
            >= 65 and <= 127 => rawValue - 128,
            _ => 0,
        },
        EncoderMode.SignMagnitude => rawValue switch
        {
            >= 1 and <= 63 => rawValue,
            >= 65 and <= 127 => -(rawValue - 64),
            _ => 0,
        },
        EncoderMode.OffsetBinary => rawValue - 64,
        _ => 0,
    };
}
