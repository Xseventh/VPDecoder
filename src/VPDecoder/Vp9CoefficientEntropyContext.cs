namespace VPDecoder;

internal sealed class Vp9CoefficientEntropyContext
{
    private readonly byte[][] _aboveContexts;
    private readonly byte[][] _leftContexts;

    private Vp9CoefficientEntropyContext(int miColumns)
    {
        _aboveContexts =
        [
            new byte[checked(miColumns * 2)],
            new byte[miColumns],
            new byte[miColumns]
        ];
        _leftContexts = [new byte[16], new byte[16], new byte[16]];
    }

    public static Vp9CoefficientEntropyContext Create(Vp9FrameHeader header)
    {
        return new Vp9CoefficientEntropyContext(header.TileInfo.MiColumns);
    }

    public int GetInitialContext(int plane, int x4, int y4, Vp9TransformSize transformSize)
    {
        ValidatePlane(plane);
        var step = GetTransformSizeIn4x4Blocks(transformSize);
        var above = HasAny(_aboveContexts[plane], x4, step) ? 1 : 0;
        var left = HasAnyWrapped(_leftContexts[plane], y4, step) ? 1 : 0;
        return above + left;
    }

    public void SetTransformContext(int plane, int x4, int y4, Vp9TransformSize transformSize, bool hasEob)
    {
        ValidatePlane(plane);
        var step = GetTransformSizeIn4x4Blocks(transformSize);
        Fill(_aboveContexts[plane], x4, step, hasEob);
        FillWrapped(_leftContexts[plane], y4, step, hasEob);
    }

    public void ClearBlock(int plane, int x4, int y4, int width4, int height4)
    {
        ValidatePlane(plane);
        Fill(_aboveContexts[plane], x4, width4, false);
        FillWrapped(_leftContexts[plane], y4, height4, false);
    }

    public static int GetTransformSizeIn4x4Blocks(Vp9TransformSize transformSize)
    {
        return transformSize switch
        {
            Vp9TransformSize.Tx4X4 => 1,
            Vp9TransformSize.Tx8X8 => 2,
            Vp9TransformSize.Tx16X16 => 4,
            Vp9TransformSize.Tx32X32 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(transformSize), transformSize, "Unsupported VP9 transform size.")
        };
    }

    private static bool HasAny(byte[] contexts, int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "VP9 coefficient context offset cannot be negative.");
        }

        if (start >= contexts.Length)
        {
            return false;
        }

        var available = Math.Min(length, contexts.Length - start);
        for (var i = 0; i < available; i++)
        {
            if (contexts[start + i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyWrapped(byte[] contexts, int start, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (contexts[(start + i) & 15] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Fill(byte[] contexts, int start, int length, bool value)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "VP9 coefficient context offset cannot be negative.");
        }

        if (start >= contexts.Length)
        {
            return;
        }

        var available = Math.Min(length, contexts.Length - start);
        Array.Fill(contexts, value ? (byte)1 : (byte)0, start, available);
    }

    private static void FillWrapped(byte[] contexts, int start, int length, bool value)
    {
        var byteValue = value ? (byte)1 : (byte)0;
        for (var i = 0; i < length; i++)
        {
            contexts[(start + i) & 15] = byteValue;
        }
    }

    private static void ValidatePlane(int plane)
    {
        if (plane is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }
    }
}
