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
        Assert.Equal("v1:07a303545cc88b528f912320114ca73f7601ef10ac3f46ccb1bca9a8043fc9d1", Assert.Single(blocks).Hash);
    }

    [Fact]
    public void Donkama2000()
    {
        var hashes = Tja.Hashes(Tja.Fixture("Donkama2000.tja"));
        Assert.Equal("v1:b23e05c9d702e067449de92a52889eecbf6b9a42cefc2488e639c828a9b042dc", hashes["Easy/single"]);
        Assert.Equal("v1:d3e378ea9e3a068e8598c995e939cea9153454782b64ccdfbed86ead3e4688ce", hashes["Normal/single"]);
        Assert.Equal("v1:abb3c1d129617416c14360a9b8216485e784e40fa3849b4363ac77ebbc8b796f", hashes["Hard/single"]);
        Assert.Equal("v1:3c8ebc92c87a7ae739c2b6fe2a0ab3efabf5bd9ef64cdda9276d37ae79f42bf0", hashes["Oni/single"]);
    }
}
