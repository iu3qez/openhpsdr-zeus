using Zeus.Contracts;

namespace Zeus.Plugins.Contracts;

/// <summary>
/// Full radio command surface for plugins that declare
/// <see cref="PluginCapabilities.ControlRadio"/>.
/// Superset of <see cref="IRadioController"/>; replaces it from ABI 2.
///
/// Every method matches a public method on RadioService or TxService.
/// The adapter (RadioCommandSurfaceAdapter in Zeus.Server.Hosting)
/// delegates to those services directly — no HTTP, no DTO marshaling.
/// </summary>
public interface IRadioCommandSurface
{
    // ── State ────────────────────────────────────────────────────────
    StateDto Snapshot();

    // ── VFO ──────────────────────────────────────────────────────────
    StateDto SetVfo(long hz);

    // ── Mode / Filter ────────────────────────────────────────────────
    StateDto SetMode(RxMode mode);
    StateDto SetFilter(int lowHz, int highHz);
    StateDto SetTxFilter(int lowHz, int highHz);

    // ── TX ───────────────────────────────────────────────────────────
    bool TrySetMox(bool on, out string? error);
    bool TrySetTun(bool on, out string? error);
    void SetDrive(int percent);
    void SetTuneDrive(int percent);

    // ── RX Audio ─────────────────────────────────────────────────────
    StateDto SetRxAfGain(double db);

    // ── AGC ──────────────────────────────────────────────────────────
    StateDto SetAgcTop(double topDb);
    StateDto SetAutoAgc(bool enabled);

    // ── Preamp / Attenuator ──────────────────────────────────────────
    StateDto SetPreamp(bool on);
    StateDto SetAttenuator(int db);
    StateDto SetAutoAtt(bool enabled);

    // ── Noise Reduction ──────────────────────────────────────────────
    StateDto SetNr(NrConfig cfg);
    StateDto SetNr4(Nr4ConfigSetRequest req);

    // ── Display ──────────────────────────────────────────────────────
    StateDto SetZoom(int level);

    // ── TX extras ────────────────────────────────────────────────────
    StateDto SetTxMonitor(TxMonitorSetRequest req);
    StateDto SetTwoTone(TwoToneSetRequest req);
    StateDto SetPs(PsControlSetRequest req);

    // ── Mic ──────────────────────────────────────────────────────────
    StateDto SetTxMicGain(int db);
}
