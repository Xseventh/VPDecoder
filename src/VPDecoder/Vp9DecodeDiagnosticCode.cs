namespace VPDecoder;

public enum Vp9DecodeDiagnosticCode
{
    InvalidDecodeOptions,
    InvalidPacket,
    UnsupportedProfile,
    UnsupportedBitDepth,
    UnsupportedChromaSubsampling,
    UnsupportedInterFrameFeature,
    UnsupportedTransformMode,
    UnsupportedPredictionMode,
    UnsupportedLoopFilter,
    UnsupportedFeature,
    TruncatedPacket,
    DimensionMismatch,
    AllocationLimitExceeded,
    MissingReferenceFrame,
    InternalDecodeFailure
}
