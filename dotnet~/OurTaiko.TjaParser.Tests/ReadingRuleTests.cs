using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// Fixes to how a file is read (Documentation~/Parser-TODO.md, P0).
public class ReadingRuleTests
{
    [Theory]
    [InlineData("#MEASURE 4")]
    [InlineData("#MEASURE")]
    public void AMeasureWithoutASlashFailsTheBlockAsAFormatError(string command)
    {
        var e = Assert.Throws<FormatException>(() => TjaParser.Parse($"BPM:120\nCOURSE:Oni\n#START\n{command}\n1,\n#END\n"));
        Assert.Contains("MEASURE", e.Message);
    }
}
