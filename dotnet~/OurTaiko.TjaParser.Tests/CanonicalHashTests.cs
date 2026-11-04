using System.Security.Cryptography;
using System.Text;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// OfCanonical hashes the text's UTF-8 a buffer at a time since P2-6: any text, also one with
// surrogate pairs (or lone surrogates) across the buffer's edge, hashes as the whole text did.
public class CanonicalHashTests
{
    [Fact]
    public void HashesAsTheWholeTextEncodedAtOnce()
    {
        var r = new Random(7);
        string[] pieces = { "a", "1 ", "\n", "é", "音符", "😀", "\uD83D", "\uDE00", new string('x', 8191), new string('y', 4095) };
        for (int n = 0; n < 300; n++)
        {
            var sb = new StringBuilder();
            for (int k = r.Next(40); k > 0; k--) sb.Append(pieces[r.Next(pieces.Length)]);
            string text = sb.ToString();
            string expected = ChartHash.Prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            Assert.Equal(expected, ChartHash.OfCanonical(text));
        }
    }
}
