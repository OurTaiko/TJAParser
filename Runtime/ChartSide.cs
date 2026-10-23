namespace OurTaiko
{
    // The arcade stores every course three times: single, 1P and 2P. In a TJA a block's side is
    // #START's player (`#START P1`, `#START P2`, none for single); servers list it per block.
    // Which side a player's seat reads is the game's rule, not the file's (OurTaikoPlay SeatBlocks).
    public static class ChartSide
    {
        public static readonly string[] All = { "single", "p1", "p2" };

        // The side of a block whose #START names this player (TjaLine.Value: "", "P1", "P2", …).
        public static string OfStart(string player) => player == "P1" ? "p1" : player == "P2" ? "p2" : "single";
    }
}
