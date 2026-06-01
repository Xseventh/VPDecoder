namespace VPDecoder;

internal sealed record Vp9KeyFrameDecodeState(
    Vp9FrameHeader Header,
    Vp9CompressedHeader CompressedHeader,
    IReadOnlyList<Vp9TileBuffer> TileBuffers,
    Vp9DequantTables DequantTables,
    Vp9YuvFrameBuffer FrameBuffer)
{
    public static bool TryCreate(
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        out Vp9KeyFrameDecodeState? state,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        state = null;
        diagnostic = null;

        if (header.FrameType != Vp9FrameType.KeyFrame)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 key-frame decode state cannot be created for an inter frame.");
            return false;
        }

        if (header.Segmentation.Enabled)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 segmentation dequant tables are not supported by this decoder slice.");
            return false;
        }

        if (header.BitDepth != 8)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedBitDepth(
                "VP9 key-frame decode state currently supports only 8-bit frames.");
            return false;
        }

        if (tileBuffers.Count != header.TileInfo.TileColumns * header.TileInfo.TileRows)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 key-frame decode state received an unexpected tile count.");
            return false;
        }

        try
        {
            state = new Vp9KeyFrameDecodeState(
                header,
                compressedHeader,
                tileBuffers,
                Vp9DequantTables.Create(header.Quantization, header.BitDepth),
                Vp9YuvFrameBuffer.Create(header.Width, header.Height));
            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 YUV frame buffer size overflowed while creating decode state.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 YUV frame buffer allocation failed.");
            return false;
        }
    }
}
