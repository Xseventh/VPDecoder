namespace VPDecoder;

public sealed record Vp9TileBuffer(
    int Index,
    int? SizeFieldOffset,
    int DataOffset,
    int Size);
