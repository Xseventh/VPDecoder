namespace VPDecoder;

public static class Vp9CompressedHeaderParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader frameHeader,
        out Vp9CompressedHeader? compressedHeader,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        compressedHeader = null;
        diagnostic = null;

        if (frameHeader.HeaderSizeInBytes + frameHeader.FirstPartitionSize > packet.Length)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compressed header extends past the packet boundary.");
            return false;
        }

        try
        {
            var compressedHeaderBytes = packet.Slice(frameHeader.HeaderSizeInBytes, frameHeader.FirstPartitionSize);
            var reader = new Vp9BoolReader(compressedHeaderBytes);
            var transformMode = frameHeader.Quantization.Lossless
                ? Vp9TransformMode.Only4X4
                : ReadTransformMode(ref reader);

            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compressed header ended unexpectedly.");
                return false;
            }

            compressedHeader = new Vp9CompressedHeader(transformMode);
            return true;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    private static Vp9TransformMode ReadTransformMode(ref Vp9BoolReader reader)
    {
        var transformMode = reader.ReadLiteral(2);
        if (transformMode == (int)Vp9TransformMode.Allow32X32 && reader.ReadBit())
        {
            transformMode++;
        }

        if (transformMode > (int)Vp9TransformMode.Select)
        {
            throw new Vp9BoolReaderException(
                Vp9DecodeDiagnostic.InvalidPacket($"Invalid VP9 transform mode: {transformMode}."));
        }

        return (Vp9TransformMode)transformMode;
    }
}
