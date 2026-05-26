// SPDX-License-Identifier: GPL-2.0-or-later
//
// Zeus — OpenHPSDR Protocol-1 / Protocol-2 client.
// Copyright (C) 2025-2026 Brian Keating (EI6LF),
//                         Douglas J. Cerrato (KB2UKA), and contributors.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation, either version 2 of the License, or (at your
// option) any later version. See the LICENSE file at the root of this
// repository for the full text, or https://www.gnu.org/licenses/.
//
// Zeus is an independent reimplementation in .NET — not a fork. Its
// Protocol-1 / Protocol-2 framing, WDSP integration, meter pipelines, and
// TX behaviour were informed by studying the Thetis project
// (https://github.com/ramdor/Thetis), the authoritative reference
// implementation in the OpenHPSDR ecosystem. Zeus gratefully acknowledges
// the Thetis contributors whose work made this possible:
//
//   Richard Samphire (MW0LGE), Warren Pratt (NR0V),
//   Laurence Barker (G8NJJ),   Rick Koch (N1GP),
//   Bryan Rambo (W4WMT),       Chris Codella (W2PA),
//   Doug Wigley (W5WC),        FlexRadio Systems,
//   Richard Allen (W5SD),      Joe Torrey (WD5Y),
//   Andrew Mansfield (M0YGG),  Reid Campbell (MI0BOT),
//   Sigi Jetzlsperger (DH1KLM).
//
// Thetis itself continues the GPL-governed lineage of FlexRadio PowerSDR
// and the OpenHPSDR (TAPR/OpenHPSDR) ecosystem; that lineage is preserved
// here. See ATTRIBUTIONS.md at the repository root for the full provenance
// statement and per-component attribution.
//
// Protocol-2 / PureSignal / Saturn-class behaviour was additionally informed
// by pihpsdr (https://github.com/dl1ycf/pihpsdr), maintained by Christoph
// Wüllen (DL1YCF); and by DeskHPSDR
// (https://github.com/dl1bz/deskhpsdr), maintained by Heiko (DL1BZ).
// Both are GPL-2.0-or-later.
//
// WDSP — loaded by Zeus via P/Invoke — is Copyright (C) Warren Pratt
// (NR0V), distributed under GPL v2 or later.
//
// Zeus is distributed WITHOUT ANY WARRANTY; see the GNU General Public
// License for details.

using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Protocol1;

namespace Zeus.Server;

/// <summary>
/// Adapts <see cref="RadioService"/> + <see cref="TxService"/> into the
/// <see cref="IRadioCommandSurface"/> contract that plugins consume.
/// Registered as a singleton in the DI container — one per host lifetime.
/// </summary>
internal sealed class RadioCommandSurfaceAdapter : IRadioCommandSurface
{
    private readonly RadioService _radio;
    private readonly TxService _tx;

    public RadioCommandSurfaceAdapter(RadioService radio, TxService tx)
    {
        _radio = radio;
        _tx = tx;
    }

    // ── State ────────────────────────────────────────────────────────────
    public StateDto Snapshot() => _radio.Snapshot();

    // ── VFO ──────────────────────────────────────────────────────────────
    public StateDto SetVfo(long hz) => _radio.SetVfo(hz);

    // ── Mode / Filter ────────────────────────────────────────────────────
    public StateDto SetMode(RxMode mode) => _radio.SetMode(mode);
    public StateDto SetFilter(int lowHz, int highHz) => _radio.SetFilter(lowHz, highHz);
    public StateDto SetTxFilter(int lowHz, int highHz) => _radio.SetTxFilter(lowHz, highHz);

    // ── TX ───────────────────────────────────────────────────────────────
    public bool TrySetMox(bool on, out string? error) => _tx.TrySetMox(on, out error);
    public bool TrySetTun(bool on, out string? error) => _tx.TrySetTun(on, out error);
    public void SetDrive(int percent) => _radio.SetDrive(percent);
    public void SetTuneDrive(int percent) => _radio.SetTuneDrive(percent);

    // ── RX Audio ─────────────────────────────────────────────────────────
    public StateDto SetRxAfGain(double db) => _radio.SetRxAfGain(db);

    // ── AGC ──────────────────────────────────────────────────────────────
    public StateDto SetAgcTop(double topDb) => _radio.SetAgcTop(topDb);
    public StateDto SetAutoAgc(bool enabled) => _radio.SetAutoAgc(enabled);

    // ── Preamp / Attenuator ──────────────────────────────────────────────
    public StateDto SetPreamp(bool on) => _radio.SetPreamp(on);
    public StateDto SetAttenuator(int db) => _radio.SetAttenuator(new HpsdrAtten(db));
    public StateDto SetAutoAtt(bool enabled) => _radio.SetAutoAtt(enabled);

    // ── Noise Reduction ──────────────────────────────────────────────────
    public StateDto SetNr(NrConfig cfg) => _radio.SetNr(cfg);
    public StateDto SetNr4(Nr4ConfigSetRequest req) => _radio.SetNr4(req);

    // ── Display ──────────────────────────────────────────────────────────
    public StateDto SetZoom(int level) => _radio.SetZoom(level);

    // ── TX extras ────────────────────────────────────────────────────────
    public StateDto SetTxMonitor(TxMonitorSetRequest req) => _radio.SetTxMonitor(req);
    public StateDto SetTwoTone(TwoToneSetRequest req) => _radio.SetTwoTone(req);
    public StateDto SetPs(PsControlSetRequest req) => _radio.SetPs(req);

    // ── Mic ──────────────────────────────────────────────────────────────
    public StateDto SetTxMicGain(int db) => _radio.SetTxMicGain(db);
}
