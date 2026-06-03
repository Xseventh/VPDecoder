namespace VPDecoder;

internal sealed record Vp8KeyFrameSyntaxHeader(
    Vp8KeyFrameColorSpace ColorSpace,
    bool ClampType,
    Vp8SegmentationHeader Segmentation,
    Vp8LoopFilterHeader LoopFilter,
    int Log2TokenPartitionCount,
    int TokenPartitionCount,
    Vp8QuantizationHeader Quantization);

internal enum Vp8KeyFrameColorSpace
{
    Bt601,
    Reserved
}

internal sealed record Vp8SegmentationHeader(
    bool Enabled,
    bool UpdateMap,
    bool UpdateFeatureData,
    bool AbsoluteDeltaMode,
    int[] QuantizerUpdates,
    int[] LoopFilterUpdates,
    byte?[] SegmentTreeProbabilities);

internal sealed record Vp8LoopFilterHeader(
    Vp8LoopFilterType Type,
    int Level,
    int SharpnessLevel,
    bool DeltaEnabled,
    bool DeltaUpdate,
    int[] ReferenceFrameDeltas,
    int[] ModeDeltas);

internal enum Vp8LoopFilterType
{
    Normal,
    Simple
}

internal sealed record Vp8QuantizationHeader(
    int YAcQuantizerIndex,
    int YDcDelta,
    int Y2DcDelta,
    int Y2AcDelta,
    int UvDcDelta,
    int UvAcDelta);
