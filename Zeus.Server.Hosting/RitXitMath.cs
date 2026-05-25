// SPDX-License-Identifier: GPL-2.0-or-later
//
// Zeus — OpenHPSDR Protocol-1 / Protocol-2 client.
// Copyright (C) 2025-2026 Brian Keating (EI6LF),
//                         Douglas J. Cerrato (KB2UKA),
//                         Simone Fabris (IU3QEZ), and contributors.

namespace Zeus.Server;

internal static class RitXitMath
{
    public const int MaxOffsetHz = 3000;

    public static int ClampOffset(int hz) =>
        Math.Clamp(hz, -MaxOffsetHz, MaxOffsetHz);

    public static int FilterAwareStepHz(int filterBandwidthHz) =>
        filterBandwidthHz <= 250 ? 5 : 10;
}
