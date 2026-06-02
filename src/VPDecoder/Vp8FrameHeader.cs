namespace VPDecoder;

public sealed record Vp8FrameHeader(
    int PacketLength,
    int HeaderSizeInBytes,
    Vp8FrameType FrameType,
    int Version,
    bool ShowFrame,
    int FirstPartitionSize,
    bool SyncCodeValid,
    int Width,
    int Height,
    int HorizontalScale,
    int VerticalScale);

public enum Vp8FrameType
{
    KeyFrame,
    InterFrame
}
