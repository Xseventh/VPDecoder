namespace VPDecoder;

public sealed record Vp9FrameHeader(
    int PacketLength,
    int HeaderSizeInBytes,
    int FrameMarker,
    int Profile,
    bool ShowExistingFrame,
    int? ExistingFrameIndex,
    Vp9FrameType FrameType,
    bool ShowFrame,
    bool ErrorResilientMode,
    bool SyncCodeValid,
    int BitDepth,
    Vp9ColorSpace ColorSpace,
    Vp9ColorRange ColorRange,
    int SubsamplingX,
    int SubsamplingY,
    int Width,
    int Height,
    int RenderWidth,
    int RenderHeight,
    bool RefreshFrameContext,
    int RefreshFrameFlags,
    bool FrameParallelDecodingMode,
    int FrameContextIndex,
    Vp9LoopFilterHeader LoopFilter,
    Vp9QuantizationHeader Quantization,
    Vp9SegmentationHeader Segmentation,
    Vp9TileInfo TileInfo,
    int FirstPartitionSize,
    bool IntraOnly,
    int ResetFrameContextMode,
    IReadOnlyList<int> ReferenceFrameIndices,
    IReadOnlyList<bool> ReferenceFrameSignBiases,
    IReadOnlyList<bool> FrameSizeReferenceFlags,
    int? FrameSizeReferenceIndex,
    bool RenderSizeDifferent,
    bool AllowHighPrecisionMv,
    Vp9InterpolationFilter InterpolationFilter);

public enum Vp9FrameType
{
    KeyFrame,
    InterFrame
}

public enum Vp9ColorSpace
{
    Unknown = 0,
    Bt601 = 1,
    Bt709 = 2,
    Smpte170 = 3,
    Smpte240 = 4,
    Bt2020 = 5,
    Reserved = 6,
    Srgb = 7
}

public enum Vp9ColorRange
{
    Studio = 0,
    Full = 1
}

public enum Vp9InterpolationFilter
{
    None = -1,
    EightTapSmooth = 0,
    EightTap = 1,
    EightTapSharp = 2,
    Bilinear = 3,
    Switchable = 4
}

public sealed record Vp9ReferenceFrameInfo(
    int Width,
    int Height);

public sealed record Vp9LoopFilterHeader(
    int FilterLevel,
    int SharpnessLevel,
    bool ModeRefDeltaEnabled,
    bool ModeRefDeltaUpdate,
    IReadOnlyList<int> RefDeltas,
    IReadOnlyList<int> ModeDeltas);

public sealed record Vp9QuantizationHeader(
    int BaseQIndex,
    int YDcDeltaQ,
    int UvDcDeltaQ,
    int UvAcDeltaQ)
{
    public bool Lossless => BaseQIndex == 0 && YDcDeltaQ == 0 && UvDcDeltaQ == 0 && UvAcDeltaQ == 0;
}

public sealed record Vp9SegmentationHeader(
    bool Enabled,
    bool UpdateMap,
    bool TemporalUpdate,
    bool UpdateData,
    bool AbsoluteData,
    IReadOnlyList<byte?> TreeProbabilities,
    IReadOnlyList<byte?> PredictionProbabilities);

public sealed record Vp9TileInfo(
    int MiColumns,
    int MiRows,
    int SuperblockColumns,
    int MinLog2TileColumns,
    int MaxLog2TileColumns,
    int Log2TileColumns,
    int Log2TileRows)
{
    public int TileColumns => 1 << Log2TileColumns;
    public int TileRows => 1 << Log2TileRows;
}
