using System.Text.RegularExpressions;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

public class HashTests
{
    [Fact]
    public void HashesCarryTheVersionAndSixtyFourHexDigits()
    {
        foreach (var block in ChartHash.OfFile(Tja.Sample))
            Assert.Matches("^v1:[0-9a-f]{64}$", block.Hash);
        Assert.Equal("v1:", ChartHash.Prefix);
    }

    [Theory]
    [InlineData("TITLE:Test Song", "TITLE:Renamed")]
    [InlineData("TITLE:Test Song", "TITLE:Test Song\nTITLEJA:テスト\nSUBTITLEJA:--誰か")]
    [InlineData("SUBTITLE:--Someone", "SUBTITLE:++Nobody")]
    [InlineData("BPM:120", "MAKER:Author\nGENRE:Variety\nBPM:120")]
    [InlineData("LEVEL:8", "LEVEL:9")]
    [InlineData("LEVEL:10", "LEVEL:7")]
    [InlineData("1020,", "1020, // a comment\n\n")]
    [InlineData("#GOGOSTART", "  #GOGOSTART  ")]
    [InlineData("COURSE:Oni\nLEVEL:8", "COURSE:3\nLEVEL:8")]
    [InlineData("COURSE:Edit", "COURSE:Ura")]
    [InlineData("WAVE:x", "WAVE:y")]
    public void LabelsCommentsAndSpellingDoNotChangeAnyHash(string from, string to)
    {
        string text = Tja.Sample.Replace("BPM:120", "WAVE:x\nBPM:120");
        var before = Tja.Hashes(text);
        var after = Tja.Hashes(text.Replace(from, to));
        Assert.Equal(before, after);
    }

    [Fact]
    public void LineEndingsAndBomDoNotChangeAnyHash()
    {
        var before = Tja.Hashes(Tja.Sample);
        Assert.Equal(before, Tja.Hashes(Tja.Sample.Replace("\n", "\r\n")));
        Assert.Equal(before, Tja.Hashes("﻿" + Tja.Sample));
    }

    [Theory]
    [InlineData("1020,", "1021,")]
    [InlineData("1020,", "10200,")]
    [InlineData("BPM:120", "BPM:121")]
    [InlineData("OFFSET:-1", "OFFSET:-1.5")]
    [InlineData("BALLOON:5", "BALLOON:6")]
    [InlineData("#GOGOSTART\n", "")]
    [InlineData("3040,", "#SCROLL 2\n3040,")]
    [InlineData("3040,", "#BPMCHANGE 180\n3040,")]
    [InlineData("3040,", "#MEASURE 3/4\n3040,")]
    [InlineData("3040,", "#DELAY 0.5\n3040,")]
    [InlineData("3040,", "#BARLINEOFF\n3040,")]
    [InlineData("3040,", "#SUDDEN 2 1\n3040,")]
    [InlineData("7008,", "5008,")]
    public void WhatIsPlayedChangesOnlyThatBlocksHash(string from, string to)
    {
        var before = Tja.Hashes(Tja.Sample);
        var after = Tja.Hashes(Tja.Sample.Replace(from, to));
        Assert.NotEqual(before["Oni/single"], after["Oni/single"]);
        // Headers before the first COURSE are every block's; a change inside the Oni block is its own.
        bool header = !from.EndsWith(",") && !from.StartsWith("#");
        if (!header)
        {
            Assert.Equal(before["Edit/single"], after["Edit/single"]);
            Assert.Equal(before["Oni/p1"], after["Oni/p1"]);
            Assert.Equal(before["Oni/p2"], after["Oni/p2"]);
        }
    }

    [Fact]
    public void ARouteChangeChangesTheBranchingHash()
    {
        string before = Tja.Hashes(Tja.Branching)["Oni/single"];
        Assert.NotEqual(before, Tja.Hashes(Tja.Branching.Replace("#E\n1010,", "#E\n1011,"))["Oni/single"]);
        Assert.NotEqual(before, Tja.Hashes(Tja.Branching.Replace("p,50,80", "p,60,80"))["Oni/single"]);
        Assert.NotEqual(before, Tja.Hashes(Tja.Branching.Replace("#M\n1111,\n", ""))["Oni/single"]);
        Assert.Equal(before, Tja.Hashes(Tja.Branching.Replace("TITLE:Branch", "TITLE:Other"))["Oni/single"]);
    }

    [Fact]
    public void TheCanonicalTextNamesNoTitleOrLevel()
    {
        string canonical = ChartCanonical.Write(TjaParser.Parse(Tja.Sample, "Oni", "single"));
        Assert.StartsWith("tjaparser-canonical 1\n", canonical);
        Assert.DoesNotContain("Test Song", canonical);
        Assert.DoesNotContain("Someone", canonical);
        Assert.DoesNotContain("Oni", canonical);
        Assert.Contains("\nnotes 5\n", canonical);
        Assert.Contains("\nbars 3\n", canonical);
        Assert.Contains("\ngogos 2\n", canonical);
    }

    [Fact]
    public void NumbersAreWrittenCultureFree()
    {
        Assert.Equal("0", ChartCanonical.N(-0.0));
        Assert.Equal("1.5", ChartCanonical.N(1.5));
        Assert.Equal("inf", ChartCanonical.N(double.PositiveInfinity));
        Assert.Equal("-inf", ChartCanonical.N(double.NegativeInfinity));
        Assert.Equal("0.1", ChartCanonical.N(0.1));
    }

    [Fact]
    public void Donkama2000HashesAndSurvivesRenaming()
    {
        string text = Tja.Fixture("Donkama2000.tja");
        var blocks = ChartHash.OfFile(text);
        Assert.NotEmpty(blocks);
        Assert.All(blocks, b => Assert.True(b.Ok, b.Error));
        string renamed = Regex.Replace(text, @"^(TITLE|SUBTITLE)[A-Z_]*:.*$", "", RegexOptions.Multiline) + "\nTITLE:New name\n";
        // A TITLE after the blocks is still a header line outside them.
        Assert.Equal(blocks.Select(b => b.Hash), ChartHash.OfFile(renamed).Select(b => b.Hash));
        Assert.NotEqual(blocks[0].Hash, ChartHash.OfFile(text.Replace("#SCROLL 1.33", "#SCROLL 1.34"))[0].Hash);
    }
}
