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
        Assert.Equal("v2:4bf447484f50655462293e106f2c034aa036bb51c3a01dd1bcc53c4c64fcfd7f", Assert.Single(blocks).Hash);
    }

    [Fact]
    public void Donkama2000()
    {
        var hashes = Tja.Hashes(Tja.Fixture("Donkama2000.tja"));
        Assert.Equal("v2:f29f9c37dc28099855ef7611b719f7e1523cb9c449801503f716ba9ea4cfb6d3", hashes["Easy/single"]);
        Assert.Equal("v2:b575da0afadbc5421c73cf58fe795f3246aa8c145da721b548d344db4be720f4", hashes["Normal/single"]);
        Assert.Equal("v2:6b53dbc0917833e87ac817669ef0343784cb236dca734986d33ad72ba05946d7", hashes["Hard/single"]);
        Assert.Equal("v2:605607239f8c0a0488599a0c8b31ad8e8729e137307c695e1e37b5598090e16f", hashes["Oni/single"]);
    }
}
