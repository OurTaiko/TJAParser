using System.Globalization;
using System.Text;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// The same chart written on other grids: every measure's slots multiplied (1010 as 10001000 or
// 100000100000), commands kept between the same notes. What is played is the same, so the
// canonical text and the hash must be: times are put on their grid from places that are not added
// up slot by slot (TjaParser's TimingState), and beat clocks come out the same to the last bit.
public class GridRewriteTests
{
    static readonly int[] Slots = { 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 24, 32, 48 };
    static readonly int[] Factors = { 2, 3, 4, 5, 6, 8 };
    static readonly string[] Lengths = { "4/4", "3/4", "7/8", "5/4", "2/4", "11/16", "6/8", "4.5/4" };

    // A chart twice: on its own grid, and with each measure on a multiple of it.
    static (string Plain, string Fine) Generate(int seed, string mode, int measures)
    {
        var random = new Random(seed);
        var plain = new StringBuilder(); var fine = new StringBuilder();
        string head = "BPM:" + Bpm(random) + "\nOFFSET:-" + (random.Next(0, 3000) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)
            + "\nCOURSE:Oni\n#START\n" + mode;
        plain.Append(head); fine.Append(head);
        void Both(string line) { plain.Append(line).Append('\n'); fine.Append(line).Append('\n'); }
        for (int m = 0; m < measures; m++)
        {
            if (random.Next(8) == 0) Both("#BPMCHANGE " + Bpm(random));
            if (random.Next(10) == 0) Both("#MEASURE " + Lengths[random.Next(Lengths.Length)]);
            if (random.Next(12) == 0) Both("#SCROLL " + (0.5 + random.Next(1, 30) / 10.0).ToString("0.#", CultureInfo.InvariantCulture));
            if (random.Next(25) == 0) Both("#DELAY " + (random.Next(-200, 900) / 10000.0).ToString("0.####", CultureInfo.InvariantCulture));
            int slots = Slots[random.Next(Slots.Length)], factor = Factors[random.Next(Factors.Length)];
            // Sometimes a command between two slots of the measure.
            int cut = slots > 1 && random.Next(6) == 0 ? random.Next(1, slots) : -1;
            string between = random.Next(3) switch
            {
                0 => "#BPMCHANGE " + Bpm(random),
                1 => "#DELAY 0.0" + random.Next(1, 99).ToString(CultureInfo.InvariantCulture),
                _ => "#MEASURE " + Lengths[random.Next(Lengths.Length)],
            };
            // A drumroll from one slot to a later one of the measure, now and then.
            int rollAt = slots > 2 && random.Next(15) == 0 ? random.Next(0, slots - 1) : -1, rollEnd = rollAt < 0 ? -1 : random.Next(rollAt + 1, slots);
            for (int i = 0; i < slots; i++)
            {
                if (i == cut) { plain.Append('\n'); fine.Append('\n'); Both(between); }
                char note = i == rollAt ? '5' : i == rollEnd ? '8' : rollAt >= 0 && i > rollAt && i < rollEnd ? '0' : "0112234"[random.Next(7)];
                plain.Append(note);
                fine.Append(note).Append('0', factor - 1);
            }
            Both(",");
        }
        Both("#END");
        return (plain.ToString(), fine.ToString());
    }

    static string Bpm(Random random)
        => random.Next(3) == 0 ? random.Next(60, 300).ToString(CultureInfo.InvariantCulture)
            : (random.Next(6000, 30000) / 100.0).ToString("0.##", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("")]
    [InlineData("#HBSCROLL\n")]
    [InlineData("#BMSCROLL\n")]
    public void AChartOnAFinerGridIsTheSameChart(string mode)
    {
        int notes = 0;
        for (int seed = 0; seed < 150; seed++)
        {
            var (plain, fine) = Generate(seed, mode, new[] { 40, 150, 600 }[seed % 3]);
            var a = TjaParser.Parse(plain); var b = TjaParser.Parse(fine);
            notes += a.Notes.Count;
            string[] expected = ChartCanonical.Write(a).Split('\n'), actual = ChartCanonical.Write(b).Split('\n');
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.True(expected[i] == actual[i], $"seed {seed}, line {i}: {expected[i]} | {actual[i]}");
            Assert.Equal(ChartHash.Of(a), ChartHash.Of(b));
        }
        Assert.True(notes > 100000, $"{notes} notes");
    }

    [Fact]
    public void BeatClocksAreTheSameToTheLastBit()
    {
        // Twelve to the measure and twenty-four: adding a twelfth three times does not give what
        // adding a twenty-fourth six times gives (0.49999999999999994 and 0.5).
        static TaikoChart Chart(string measure)
            => TjaParser.Parse("BPM:173\nCOURSE:Oni\n#START\n#HBSCROLL\n" + string.Concat(Enumerable.Repeat(measure + ",\n", 30)) + "#END\n");
        var twelve = Chart("100100100100");
        var twentyFour = Chart("100000100000100000100000");
        Assert.Equal(twelve.Notes.Select(n => BitConverter.DoubleToInt64Bits(n.ScrollClock)), twentyFour.Notes.Select(n => BitConverter.DoubleToInt64Bits(n.ScrollClock)));
        Assert.Equal(0.25, twelve.Notes[1].ScrollClock);
        Assert.Equal(29.75, twelve.Notes[^1].ScrollClock);
        Assert.Equal(ChartHash.Of(twelve), ChartHash.Of(twentyFour));
    }

    [Fact]
    public void BarLinesDoNotDependOnTheGrid()
    {
        // A measure read in one run is its length, whatever its slots: 128.57 BPM on 7 and on 48.
        static TaikoChart Chart(string measure)
            => TjaParser.Parse("BPM:128.57\nCOURSE:Oni\n#START\n" + string.Concat(Enumerable.Repeat(measure + ",\n", 400)) + "#END\n");
        var seven = Chart("1000000");
        var fortyEight = Chart("1" + new string('0', 47));
        Assert.Equal(seven.Bars.Select(b => BitConverter.DoubleToInt64Bits(b.RawMs)), fortyEight.Bars.Select(b => BitConverter.DoubleToInt64Bits(b.RawMs)));
        // And is where 400 measures of 240000 / 128.57 ms end, to the precision of a double.
        Assert.Equal(399 * (240000 / 128.57), seven.Bars[^1].RawMs, 9);
    }
}
