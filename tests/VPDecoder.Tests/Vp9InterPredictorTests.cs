namespace VPDecoder.Tests;

public sealed class Vp9InterPredictorTests
{
    [Fact]
    public void TrySelectMotionVector_ForZeroMv_ReturnsZeroVectorWithoutCandidates()
    {
        Assert.True(Vp9InterPredictor.TrySelectMotionVector(
            Vp9InterPredictionMode.ZeroMv,
            candidates: [],
            out var motionVector,
            out var diagnostic));

        Assert.Equal(new Vp9MotionVector(0, 0), motionVector);
        Assert.Null(diagnostic);
    }

    [Theory]
    [InlineData((int)Vp9InterPredictionMode.NearestMv)]
    [InlineData((int)Vp9InterPredictionMode.NearMv)]
    public void TrySelectMotionVector_WhenReferenceCandidatesAreMissing_ReturnsSpecificUnsupportedDiagnostic(int modeValue)
    {
        var mode = (Vp9InterPredictionMode)modeValue;

        Assert.False(Vp9InterPredictor.TrySelectMotionVector(
            mode,
            candidates: [],
            out var motionVector,
            out var diagnostic));

        Assert.Equal(default, motionVector);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains(mode == Vp9InterPredictionMode.NearestMv ? "NEARESTMV" : "NEARMV", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySelectMotionVector_ForNearestMv_ReturnsFirstCandidate()
    {
        var candidates = new[]
        {
            new Vp9MotionVector(8, -16),
            new Vp9MotionVector(24, 32)
        };

        Assert.True(Vp9InterPredictor.TrySelectMotionVector(
            Vp9InterPredictionMode.NearestMv,
            candidates,
            out var motionVector,
            out var diagnostic));

        Assert.Equal(candidates[0], motionVector);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void TrySelectMotionVector_ForNearMv_ReturnsSecondCandidate()
    {
        var candidates = new[]
        {
            new Vp9MotionVector(8, -16),
            new Vp9MotionVector(24, 32)
        };

        Assert.True(Vp9InterPredictor.TrySelectMotionVector(
            Vp9InterPredictionMode.NearMv,
            candidates,
            out var motionVector,
            out var diagnostic));

        Assert.Equal(candidates[1], motionVector);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void TrySelectMotionVector_ForNewMv_ReturnsSpecificUnsupportedDiagnostic()
    {
        Assert.False(Vp9InterPredictor.TrySelectMotionVector(
            Vp9InterPredictionMode.NewMv,
            candidates: [new Vp9MotionVector(8, -16), new Vp9MotionVector(24, 32)],
            out var motionVector,
            out var diagnostic));

        Assert.Equal(default, motionVector);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("NEWMV", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSpatialMotionVectorCandidates_UsesLibvpxReferenceOrderFor8X8Blocks()
    {
        var left = CreateModeBlock(1, 0, Vp9InterReferenceFrame.Last, new Vp9MotionVector(8, -16));
        var above = CreateModeBlock(0, 1, Vp9InterReferenceFrame.Last, new Vp9MotionVector(24, 32));
        var differentReference = CreateModeBlock(1, 2, Vp9InterReferenceFrame.Golden, new Vp9MotionVector(40, 48));
        var current = CreateModeBlock(1, 1, Vp9InterReferenceFrame.Last);

        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            current,
            [left, above, differentReference]);

        Assert.Equal([above.MotionVector!.Value, left.MotionVector!.Value], candidates);
    }

    [Fact]
    public void BuildSpatialMotionVectorCandidates_UsesBlockSizeSpecificLibvpxReferencePositions()
    {
        var aboveRight = CreateModeBlock(
            0,
            4,
            Vp9InterReferenceFrame.Last,
            new Vp9MotionVector(8, 16),
            Vp9BlockSize.Block32X32);
        var leftBelow = CreateModeBlock(
            4,
            0,
            Vp9InterReferenceFrame.Last,
            new Vp9MotionVector(24, 32),
            Vp9BlockSize.Block32X32);
        var immediateAboveLeft = CreateModeBlock(3, 3, Vp9InterReferenceFrame.Last, new Vp9MotionVector(40, 48));
        var current = CreateModeBlock(4, 4, Vp9InterReferenceFrame.Last, blockSize: Vp9BlockSize.Block32X32);

        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            current,
            [immediateAboveLeft, aboveRight, leftBelow]);

        Assert.Equal([aboveRight.MotionVector!.Value, leftBelow.MotionVector!.Value], candidates);
    }

    [Fact]
    public void BuildSpatialMotionVectorCandidates_IgnoresCandidatesFromOtherTiles()
    {
        var otherTileAbove = CreateModeBlock(
            0,
            1,
            Vp9InterReferenceFrame.Last,
            new Vp9MotionVector(8, -16),
            tileIndex: 1);
        var current = CreateModeBlock(1, 1, Vp9InterReferenceFrame.Last, tileIndex: 0);

        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            current,
            [otherTileAbove]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildSpatialMotionVectorCandidates_IgnoresMissingVectorsAndDeduplicates()
    {
        var left = CreateModeBlock(1, 0, Vp9InterReferenceFrame.Last, new Vp9MotionVector(8, -16));
        var above = CreateModeBlock(0, 1, Vp9InterReferenceFrame.Last, new Vp9MotionVector(8, -16));
        var missingVector = CreateModeBlock(1, 2, Vp9InterReferenceFrame.Last);
        var current = CreateModeBlock(1, 1, Vp9InterReferenceFrame.Last);

        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            current,
            [left, above, missingVector]);

        Assert.Equal([left.MotionVector!.Value], candidates);
    }

    [Fact]
    public void TrySelectMotionVector_ForNearMvWithDerivedSpatialCandidates_ReturnsAboveCandidate()
    {
        var left = CreateModeBlock(1, 0, Vp9InterReferenceFrame.Last, new Vp9MotionVector(8, -16));
        var above = CreateModeBlock(0, 1, Vp9InterReferenceFrame.Last, new Vp9MotionVector(24, 32));
        var current = CreateModeBlock(1, 1, Vp9InterReferenceFrame.Last);
        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(current, [left, above]);

        Assert.True(Vp9InterPredictor.TrySelectMotionVector(
            Vp9InterPredictionMode.NearMv,
            candidates,
            out var motionVector,
            out var diagnostic));

        Assert.Equal(left.MotionVector!.Value, motionVector);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void TryResolveReferenceFrame_MapsSingleReferenceKindsToHeaderSlots()
    {
        var store = new Vp9ReferenceFrameStore();
        var last = CreateYuvFrame(width: 4, height: 4, yValue: 11);
        var golden = CreateYuvFrame(width: 4, height: 4, yValue: 22);
        var altRef = CreateYuvFrame(width: 4, height: 4, yValue: 33);
        store.Refresh(last, Vp9ColorRange.Studio, refreshFrameFlags: 0b0000_0001);
        store.Refresh(golden, Vp9ColorRange.Studio, refreshFrameFlags: 0b0000_0010);
        store.Refresh(altRef, Vp9ColorRange.Studio, refreshFrameFlags: 0b1000_0000);
        var header = CreateInterHeader(referenceSlots: [0, 1, 7]);

        Assert.True(Vp9InterPredictor.TryResolveReferenceFrame(store, header, Vp9InterReferenceFrame.Last, out var lastReference, out var lastDiagnostic), lastDiagnostic?.Message);
        Assert.True(Vp9InterPredictor.TryResolveReferenceFrame(store, header, Vp9InterReferenceFrame.Golden, out var goldenReference, out var goldenDiagnostic), goldenDiagnostic?.Message);
        Assert.True(Vp9InterPredictor.TryResolveReferenceFrame(store, header, Vp9InterReferenceFrame.AltRef, out var altReference, out var altDiagnostic), altDiagnostic?.Message);

        Assert.Equal(11, lastReference!.Frame.Pixels[0]);
        Assert.Equal(22, goldenReference!.Frame.Pixels[0]);
        Assert.Equal(33, altReference!.Frame.Pixels[0]);
    }

    [Fact]
    public void TryResolveReferenceFrame_WhenSlotIsMissing_ReturnsMissingReferenceFrame()
    {
        var store = new Vp9ReferenceFrameStore();
        store.Refresh(CreateYuvFrame(width: 4, height: 4, yValue: 11), Vp9ColorRange.Studio, refreshFrameFlags: 0b0000_0001);
        var header = CreateInterHeader(referenceSlots: [0, 1, 7]);

        Assert.False(Vp9InterPredictor.TryResolveReferenceFrame(store, header, Vp9InterReferenceFrame.Golden, out var referenceFrame, out var diagnostic));

        Assert.Null(referenceFrame);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic?.Code);
        Assert.Contains("slot 1", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(8, -16, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, -7, false)]
    public void IsWholePixelMotionVector_ChecksEighthPixelAlignment(int row, int column, bool expected)
    {
        Assert.Equal(expected, Vp9InterPredictor.IsWholePixelMotionVector(new Vp9MotionVector(row, column)));
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

    private static Vp9InterBlockModeInfoProbe CreateModeBlock(
        int miRow,
        int miColumn,
        Vp9InterReferenceFrame referenceFrame,
        Vp9MotionVector? motionVector = null,
        Vp9BlockSize blockSize = Vp9BlockSize.Block8X8,
        int tileIndex = 0)
    {
        var modeInfo = new Vp9InterModeInfoProbe(
            blockSize,
            Skip: true,
            SkipContext: 0,
            IsInterBlock: true,
            IntraInterContext: 0,
            Vp9TransformSize.Tx4X4,
            TransformSizeContext: 0,
            Vp9ReferenceMode.Single,
            referenceFrame,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            Vp9InterPredictionMode.ZeroMv,
            InterModeContext: 0,
            Vp9InterpolationFilter.EightTap);

        return new Vp9InterBlockModeInfoProbe(
            TileIndex: tileIndex,
            miRow,
            miColumn,
            PartitionPath: [Vp9PartitionType.None],
            modeInfo,
            motionVector);
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
