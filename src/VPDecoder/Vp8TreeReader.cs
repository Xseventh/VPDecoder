namespace VPDecoder;

internal static class Vp8TreeReader
{
    public static int ReadTree(
        ref Vp8BoolReader reader,
        ReadOnlySpan<sbyte> tree,
        ReadOnlySpan<byte> probabilities,
        string syntaxName)
    {
        var index = 0;
        while (true)
        {
            var probabilityIndex = index >> 1;
            if (probabilityIndex >= probabilities.Length)
            {
                throw new Vp8BoolReaderException(
                    Vp8DecodeDiagnostic.InvalidPacket(
                        $"VP8 {syntaxName} probability index is outside the probability table."));
            }

            var next = tree[index + (reader.Read(probabilities[probabilityIndex]) ? 1 : 0)];
            if (next <= 0)
            {
                return -next;
            }

            index = next;
        }
    }
}
