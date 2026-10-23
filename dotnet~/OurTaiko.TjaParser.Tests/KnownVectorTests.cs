using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// Pinned hashes. A failure here means the hash changed for every stored chart: either revert
// the change, or bump ChartCanonical.Version, re-pin these and have the servers recompute.
public class KnownVectorTests
{
    [Fact]
    public void OneMeasureOfDonKa()
    {
        var blocks = ChartHash.OfFile("BPM:120\nCOURSE:Oni\n#START\n1020,\n#END\n");
        Assert.Equal("v1:6a2f194ff64790dd87d3816b44715d7fd8bb0049057858e47e797b0dd9360f4a", Assert.Single(blocks).Hash);
    }

    [Fact]
    public void Donkama2000()
    {
        var hashes = Tja.Hashes(Tja.Fixture("Donkama2000.tja"));
        Assert.Equal("v1:b08e1988f9dcdd5dc356f6ece1966f7fcba6bc57ddad4c35243e5f93059cf13c", hashes["Easy/single"]);
        Assert.Equal("v1:4973074e513f04283d0c5239cb8fe2f2a96e93bae3e2f40a3d2d37b2f2960143", hashes["Normal/single"]);
        Assert.Equal("v1:5afb398668c002ad49dbf08fd8a665aff8ea6507a9da62fffa55eb8d4781661d", hashes["Hard/single"]);
        Assert.Equal("v1:848c794d12501518e84f257ac0f4fc211657b24f20dfce661d5649a898549a47", hashes["Oni/single"]);
    }
}
