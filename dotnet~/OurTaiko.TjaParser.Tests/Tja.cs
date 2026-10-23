using OurTaiko;

namespace OurTaiko.Tests;

static class Tja
{
    // Oni single with a balloon, a gogo and a drumroll; Edit single; Oni P1 and P2.
    public const string Sample = @"TITLE:Test Song
SUBTITLE:--Someone
BPM:120
OFFSET:-1
COURSE:Oni
LEVEL:8
BALLOON:5
#START
1020,
#GOGOSTART
3040,
#GOGOEND
7008,
#END
COURSE:Edit
LEVEL:10
#START
1111,
#END
COURSE:Oni
#START P1
1,
#END
COURSE:Oni
#START P2
2,
#END
";

    public const string Branching = @"TITLE:Branch
BPM:150
COURSE:Oni
#START
1111,
#BRANCHSTART p,50,80
#N
1000,
#E
1010,
#M
1111,
#BRANCHEND
2222,
#END
";

    public static Dictionary<string, ChartBlockHash> Blocks(string text)
        => ChartHash.OfFile(text).ToDictionary(b => b.Course + "/" + b.Side);

    public static Dictionary<string, string> Hashes(string text)
        => ChartHash.OfFile(text).ToDictionary(b => b.Course + "/" + b.Side, b => b.Hash);

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
