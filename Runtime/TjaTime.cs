using System;

namespace OurTaiko
{
    // Chart times are kept in seconds, on a grid of a thousandth of a millisecond (ChartCanonical 2).
    // OpenTaiko cuts them to whole milliseconds (CChip.n発声時刻ms), which puts every note up to a
    // millisecond early and lets the same rhythm written on another grid land a millisecond apart.
    // The play clock is still read in whole milliseconds, cut toward zero, as OpenTaiko's (long) one.
    public static class TjaTime
    {
        // A chart time from milliseconds: the nearest point of the grid, halves away from zero.
        // The division is of two whole numbers, so the result is the double nearest that point.
        public static double OnGrid(double ms) => Math.Round(ms * 1000, MidpointRounding.AwayFromZero) / 1e6;

        // A chart time in milliseconds, exact to the grid: a time on a whole millisecond is a whole
        // number, without the noise multiplying seconds by 1000 leaves. Like OnGrid it rounds to a
        // whole number and divides once, so every runtime gives the same double.
        public static double Millis(double seconds) => Math.Round(seconds * 1e6, MidpointRounding.AwayFromZero) / 1000.0;

        // A time in whole milliseconds. Rounding to a millionth of a millisecond first removes the
        // floating-point noise of a value that already is on a whole millisecond.
        public static long Ms(double seconds) => (long)Math.Truncate(Math.Round(seconds * 1000, 6));

        public static double Truncate(double seconds) => Ms(seconds) / 1000.0;
    }
}
