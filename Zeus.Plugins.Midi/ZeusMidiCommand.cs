namespace Zeus.Plugins.Midi;

public enum ZeusMidiCommand : byte
{
    // ── TX ────────────────────────────────────────────────
    Mox,
    Tune,
    TwoTone,
    PureSignal,

    // ── NR / NB ──────────────────────────────────────────
    Nr1,
    Nr2,
    Nr3,
    Nr4,
    Nb1,
    Nb2,
    Anf,
    Snb,

    // ── AGC ──────────────────────────────────────────────
    AutoAgc,
    AutoAtt,
    Preamp,

    // ── Mode selects ─────────────────────────────────────
    ModeNext,
    ModePrev,
    ModeLsb,
    ModeUsb,
    ModeCwl,
    ModeCwu,
    ModeAm,
    ModeFm,
    ModeSam,
    ModeDsb,
    ModeDigl,
    ModeDigu,

    // ── Filter ───────────────────────────────────────────
    FilterWider,
    FilterNarrower,

    // ── Zoom ─────────────────────────────────────────────
    ZoomIn,
    ZoomOut,

    // ── VFO buttons ──────────────────────────────────────
    VfoAUp100k,
    VfoADown100k,

    // ── Band ─────────────────────────────────────────────
    BandUp,
    BandDown,
    Band160m,
    Band80m,
    Band60m,
    Band40m,
    Band30m,
    Band20m,
    Band17m,
    Band15m,
    Band12m,
    Band10m,
    Band6m,
    Band2m,

    // ── Mute ─────────────────────────────────────────────
    Mute,

    // ── Knobs (absolute 0–127 → range) ──────────────────
    AfGain,
    AgcLevel,
    Drive,
    TuneDrive,
    MicGain,
    TxMonitor,
    Nr4Amount,
    Zoom,
    Attenuator,

    // ── Wheels (relative ±delta) ─────────────────────────
    VfoATune,
    VfoAMultiStep,
    FilterBandwidth,
    FilterHigh,
    FilterLow,
    FilterShift,
    TxFilterHigh,
    TxFilterLow,
    ZoomWheel,
    AfGainWheel,
    AgcLevelWheel,
    DriveWheel,

    // ── Meta ─────────────────────────────────────────────
    WheelSensUp,
    WheelSensDown,
    WheelSensToggle,
}
