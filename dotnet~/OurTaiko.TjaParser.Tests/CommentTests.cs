using System.Linq;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// `//` as in OpenTaiko, `/* */` as our own addition, and OpenTaiko's line endings.
public class CommentTests
{
    static string[] Texts(string text) => TjaLines.Read(text).Select(l => l.Text).ToArray();

    [Theory]
    [InlineData("1020,", "10/* a comment */20,")]
    [InlineData("1020,", "1020, /* a comment // with a slash pair */")]
    [InlineData("1020,", "/* one\nspanning\nlines */1020,")]
    [InlineData("1020,", "10/*\n#GOGOSTART\n*/20,")]
    [InlineData("BPM:120", "BPM:/* tempo */120")]
    [InlineData("COURSE:Edit", "/*\nCOURSE:Oni\n#START\n9,\n#END\n*/COURSE:Edit")]
    public void BlockCommentsDoNotChangeAnyHash(string from, string to)
    {
        Assert.Equal(Tja.Hashes(Tja.Sample), Tja.Hashes(Tja.Sample.Replace(from, to)));
    }

    [Fact]
    public void ALoneCarriageReturnEndsALine()
    {
        Assert.Equal(Tja.Hashes(Tja.Sample), Tja.Hashes(Tja.Sample.Replace("\n", "\r")));
        Assert.Equal(new[] { 1, 3, 4 }, TjaLines.Read("TITLE:x\r\rCOURSE:Oni\r\nLEVEL:8").Select(l => l.Line));
    }

    [Fact]
    public void AnInlineBlockCommentLeavesASpace()
    {
        Assert.Equal(new[] { "#START", "#BPMCHANGE 180", "1 0,", "#END" },
            Texts("#START\n#BPMCHANGE/**/180\n1/* x */0,\n#END"));
    }

    [Fact]
    public void ABlockCommentAcrossLinesKeepsBothEndsOnTheirOwnLines()
    {
        var lines = TjaLines.Read("#START\n1/* a\nb\nc */2,\n#END").ToList();
        Assert.Equal(new[] { "#START", "1", "2,", "#END" }, lines.Select(l => l.Text));
        Assert.Equal(new[] { 1, 2, 4, 5 }, lines.Select(l => l.Line));
    }

    [Theory]
    [InlineData("1, // /* not a block\n2,", new[] { "1,", "2," })]
    [InlineData("/* // */1,", new[] { "1," })]
    [InlineData("/*/ 1, */2,", new[] { "2," })]
    [InlineData("//* 1,\n2,", new[] { "2," })]
    [InlineData("1, */\n2,", new[] { "1, */", "2," })]
    public void TheFirstOpenerWins(string body, string[] expected)
    {
        Assert.Equal(expected.Prepend("#START").Append("#END"), Texts("#START\n" + body + "\n#END"));
    }

    [Fact]
    public void AnUnclosedBlockCommentRunsToTheEndOfTheFile()
    {
        var lines = TjaLines.Read("COURSE:Oni\n#START\n1,\n/* 2,\n#END\nCOURSE:Edit\n#START\n3,\n#END").ToList();
        Assert.Equal(new[] { "COURSE:Oni", "#START", "1,", "#END" }, lines.Select(l => l.Text));
        Assert.True(lines[3].Implicit);
    }
}
