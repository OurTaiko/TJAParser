using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // One #LANE_ALPHA (an OurTaiko command, not in OpenTaiko): the lane background's opacity goes
    // linearly to Alpha from StartTime to EndTime, from whatever it is at StartTime. Time is where
    // the command stands; a positive duration fades from there (StartTime = Time), a negative one
    // fades up to there (EndTime = Time), and 0 switches at Time (StartTime = EndTime = Time).
    // Seconds, on whole milliseconds. Cosmetic: not in the canonical text or the content hash.
    public sealed class ChartLaneAlpha
    {
        public double Time, StartTime, EndTime;
        // 0 (transparent) to 1 (opaque): the written percentage / 100.
        public double Alpha;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    // The lane background's opacity at a time: the reference reading of TaikoChart.LaneAlphas.
    public static class LaneAlpha
    {
        // Before any command the lane background is fully opaque.
        public const double Initial = 1;

        // The commands are in StartTime order (source order at one time). The one that started last
        // is in charge: it fades from the opacity the earlier ones gave at its StartTime, so a fade
        // still running when another starts stops where it is. `plays` picks the commands of the
        // routes being played (common ones have BranchId -1); without it every command counts.
        public static double At(IReadOnlyList<ChartLaneAlpha> commands, double time, Func<ChartLaneAlpha, bool> plays = null)
        {
            ChartLaneAlpha current = null;
            double from = Initial;
            foreach (var command in commands)
            {
                if (command.StartTime > time) break;
                if (plays != null && !plays(command)) continue;
                from = Value(current, from, command.StartTime);
                current = command;
            }
            return Value(current, from, time);
        }

        static double Value(ChartLaneAlpha command, double from, double time)
        {
            if (command == null) return from;
            if (time >= command.EndTime) return command.Alpha;
            return from + (command.Alpha - from) * (time - command.StartTime) / (command.EndTime - command.StartTime);
        }
    }
}
