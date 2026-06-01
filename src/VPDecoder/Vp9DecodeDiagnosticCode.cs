namespace VPDecoder;

public enum Vp9DecodeDiagnosticCode
{
    InvalidPacket,
    UnsupportedProfile,
    UnsupportedBitDepth,
    UnsupportedChromaSubsampling,
    UnsupportedInterFrameFeature,
    UnsupportedFeature,
    TruncatedPacket,
    DimensionMismatch,
    InternalDecodeFailure
}
