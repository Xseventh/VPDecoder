namespace VPDecoder.Tests;

public sealed class Vp9ReferenceFrameStoreTests
{
    [Fact]
    public void Refresh_UpdatesOnlySelectedReferenceSlots()
    {
        var store = new Vp9ReferenceFrameStore();
        var first = CreateYuvFrame(width: 4, height: 4, yValue: 10);
        var second = CreateYuvFrame(width: 8, height: 4, yValue: 20);
        store.Refresh(first, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        store.Refresh(second, Vp9ColorRange.Full, refreshFrameFlags: 0b0000_1010);

        var infos = store.CreateFrameInfos();
        Assert.Equal(4, infos[0]!.Width);
        Assert.Equal(8, infos[1]!.Width);
        Assert.Equal(4, infos[2]!.Width);
        Assert.Equal(8, infos[3]!.Width);
        Assert.Equal(4, infos[4]!.Width);
    }

    [Fact]
    public void Refresh_StoresCloneOfCallerFrame()
    {
        var store = new Vp9ReferenceFrameStore();
        var frame = CreateYuvFrame(width: 4, height: 4, yValue: 10);

        store.Refresh(frame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);
        frame.Pixels[0] = 99;

        Assert.True(store.TryGet(0, out var referenceFrame));
        Assert.Equal(10, referenceFrame!.Frame.Pixels[0]);
    }

    [Fact]
    public void Refresh_WhenCloneFrameIsFalse_StoresCallerFrame()
    {
        var store = new Vp9ReferenceFrameStore();
        var frame = CreateYuvFrame(width: 4, height: 4, yValue: 10);

        store.Refresh(frame, Vp9ColorSpace.Bt601, Vp9ColorRange.Studio, refreshFrameFlags: 0x01, cloneFrame: false);
        frame.Pixels[0] = 99;

        Assert.True(store.TryGet(0, out var referenceFrame));
        Assert.Equal(99, referenceFrame!.Frame.Pixels[0]);
    }

    [Fact]
    public void Reset_ClearsReferenceSlots()
    {
        var store = new Vp9ReferenceFrameStore();
        store.Refresh(CreateYuvFrame(width: 4, height: 4, yValue: 10), Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        store.Reset();

        Assert.False(store.TryGet(0, out _));
        Assert.All(store.CreateFrameInfos(), Assert.Null);
    }

    [Fact]
    public void ValidateInterFrameReferences_WhenReferencedSlotIsMissing_ReturnsMissingReferenceFrame()
    {
        var store = new Vp9ReferenceFrameStore();
        store.Refresh(CreateYuvFrame(width: 4, height: 4, yValue: 10), Vp9ColorRange.Studio, refreshFrameFlags: 0x01);
        var header = CreateInterHeader(referenceSlots: [0, 1, 0]);

        var diagnostic = store.ValidateInterFrameReferences(header);

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic.Code);
        Assert.Contains("slot 1", diagnostic.Message);
    }

    private static Vp9DecodedFrame CreateYuvFrame(int width, int height, byte yValue)
    {
        var uvWidth = (width + 1) / 2;
        var uvHeight = (height + 1) / 2;
        var yLength = width * height;
        var uLength = uvWidth * uvHeight;
        var vLength = uLength;
        var pixels = new byte[yLength + uLength + vLength];
        pixels.AsSpan(0, yLength).Fill(yValue);

        return Vp9DecodedFrame.CreateYuv420(
            width,
            height,
            pixels,
            new Vp9DecodedPlane(Vp9Plane.Y, width, height, width, 0, yLength),
            new Vp9DecodedPlane(Vp9Plane.U, uvWidth, uvHeight, uvWidth, yLength, uLength),
            new Vp9DecodedPlane(Vp9Plane.V, uvWidth, uvHeight, uvWidth, yLength + uLength, vLength));
    }

    private static Vp9FrameHeader CreateInterHeader(IReadOnlyList<int> referenceSlots)
    {
        return new Vp9FrameHeader(
            PacketLength: 0,
            HeaderSizeInBytes: 0,
            FrameMarker: 2,
            Profile: 0,
            ShowExistingFrame: false,
            ExistingFrameIndex: null,
            FrameType: Vp9FrameType.InterFrame,
            ShowFrame: true,
            ErrorResilientMode: false,
            SyncCodeValid: false,
            BitDepth: 8,
            ColorSpace: Vp9ColorSpace.Bt601,
            ColorRange: Vp9ColorRange.Studio,
            SubsamplingX: 1,
            SubsamplingY: 1,
            Width: 4,
            Height: 4,
            RenderWidth: 4,
            RenderHeight: 4,
            RefreshFrameContext: true,
            RefreshFrameFlags: 0,
            FrameParallelDecodingMode: false,
            FrameContextIndex: 0,
            LoopFilter: new Vp9LoopFilterHeader(0, 0, false, false, [], []),
            Quantization: new Vp9QuantizationHeader(1, 0, 0, 0),
            Segmentation: new Vp9SegmentationHeader(false, false, false, false, false, [], []),
            TileInfo: new Vp9TileInfo(1, 1, 1, 0, 0, 0, 0),
            FirstPartitionSize: 1,
            IntraOnly: false,
            ResetFrameContextMode: 0,
            ReferenceFrameIndices: referenceSlots,
            ReferenceFrameSignBiases: [false, false, false],
            FrameSizeReferenceFlags: [false, false, false],
            FrameSizeReferenceIndex: null,
            RenderSizeDifferent: false,
            AllowHighPrecisionMv: false,
            InterpolationFilter: Vp9InterpolationFilter.EightTap);
    }
}
