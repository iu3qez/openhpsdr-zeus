// SPDX-License-Identifier: GPL-2.0-or-later
//
// Zeus — OpenHPSDR Protocol-1 / Protocol-2 client.
// Copyright (C) 2025-2026 Brian Keating (EI6LF),
//                         Douglas J. Cerrato (KB2UKA),
//                         Simone Fabris (IU3QEZ), and contributors.

using Zeus.Server;

namespace Zeus.Server.Tests;

public class RitXitMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2000, 2000)]
    [InlineData(-2000, -2000)]
    [InlineData(3000, 3000)]
    [InlineData(-3000, -3000)]
    [InlineData(3001, 3000)]
    [InlineData(-3001, -3000)]
    [InlineData(50000, 3000)]
    [InlineData(-50000, -3000)]
    public void ClampOffset_clamps_to_max(int input, int expected)
    {
        Assert.Equal(expected, RitXitMath.ClampOffset(input));
    }

    [Theory]
    [InlineData(2800, 10)]
    [InlineData(500, 10)]
    [InlineData(251, 10)]
    [InlineData(250, 5)]
    [InlineData(200, 5)]
    [InlineData(100, 5)]
    [InlineData(50, 5)]
    public void FilterAwareStepHz_returns_5_for_narrow_filters(int bwHz, int expected)
    {
        Assert.Equal(expected, RitXitMath.FilterAwareStepHz(bwHz));
    }
}
