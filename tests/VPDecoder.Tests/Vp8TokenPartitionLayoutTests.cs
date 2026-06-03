namespace VPDecoder.Tests;

public sealed class Vp8TokenPartitionLayoutTests
{
    [Fact]
    public void TryCreate_WhenSinglePartition_ReturnsRemainingPayload()
    {
        var packet = new byte[15];
        var header = CreateHeader(packet.Length, firstPartitionSize: 2);
        var syntaxHeader = CreateSyntaxHeader(log2TokenPartitionCount: 0);

        var created = Vp8TokenPartitionLayoutBuilder.TryCreate(
            packet,
            header,
            syntaxHeader,
            out var layout,
            out var diagnostic);

        Assert.True(created);
        Assert.Null(diagnostic);
        var partition = Assert.Single(layout!.Partitions);
        Assert.Equal(0, partition.Index);
        Assert.Equal(12, partition.Offset);
        Assert.Equal(3, partition.Size);
    }

    [Fact]
    public void TryCreate_WhenMultiplePartitions_ReturnsExplicitAndTrailingSizes()
    {
        var packet = new byte[25];
        packet[12] = 4;
        var header = CreateHeader(packet.Length, firstPartitionSize: 2);
        var syntaxHeader = CreateSyntaxHeader(log2TokenPartitionCount: 1);

        var created = Vp8TokenPartitionLayoutBuilder.TryCreate(
            packet,
            header,
            syntaxHeader,
            out var layout,
            out var diagnostic);

        Assert.True(created);
        Assert.Null(diagnostic);
        Assert.Equal(2, layout!.Partitions.Count);
        Assert.Equal(new Vp8TokenPartition(0, Offset: 15, Size: 4), layout.Partitions[0]);
        Assert.Equal(new Vp8TokenPartition(1, Offset: 19, Size: 6), layout.Partitions[1]);
    }

    [Fact]
    public void TryCreate_WhenSizeTableIsTruncated_ReturnsTruncatedPacket()
    {
        var packet = new byte[14];
        var header = CreateHeader(packet.Length, firstPartitionSize: 2);
        var syntaxHeader = CreateSyntaxHeader(log2TokenPartitionCount: 1);

        var created = Vp8TokenPartitionLayoutBuilder.TryCreate(
            packet,
            header,
            syntaxHeader,
            out var layout,
            out var diagnostic);

        Assert.False(created);
        Assert.Null(layout);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryCreate_WhenPartitionExtendsPastPacket_ReturnsTruncatedPacket()
    {
        var packet = new byte[18];
        packet[12] = 9;
        var header = CreateHeader(packet.Length, firstPartitionSize: 2);
        var syntaxHeader = CreateSyntaxHeader(log2TokenPartitionCount: 1);

        var created = Vp8TokenPartitionLayoutBuilder.TryCreate(
            packet,
            header,
            syntaxHeader,
            out var layout,
            out var diagnostic);

        Assert.False(created);
        Assert.Null(layout);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    private static Vp8FrameHeader CreateHeader(int packetLength, int firstPartitionSize)
    {
        return new Vp8FrameHeader(
            packetLength,
            HeaderSizeInBytes: 10,
            Vp8FrameType.KeyFrame,
            Version: 0,
            ShowFrame: true,
            firstPartitionSize,
            SyncCodeValid: true,
            Width: 16,
            Height: 16,
            HorizontalScale: 0,
            VerticalScale: 0);
    }

    private static Vp8KeyFrameSyntaxHeader CreateSyntaxHeader(int log2TokenPartitionCount)
    {
        return new Vp8KeyFrameSyntaxHeader(
            Vp8KeyFrameColorSpace.Bt601,
            ClampType: false,
            new Vp8SegmentationHeader(
                Enabled: false,
                UpdateMap: false,
                UpdateFeatureData: false,
                AbsoluteDeltaMode: false,
                QuantizerUpdates: [0, 0, 0, 0],
                LoopFilterUpdates: [0, 0, 0, 0],
                SegmentTreeProbabilities: [null, null, null]),
            new Vp8LoopFilterHeader(
                Vp8LoopFilterType.Normal,
                Level: 0,
                SharpnessLevel: 0,
                DeltaEnabled: false,
                DeltaUpdate: false,
                ReferenceFrameDeltas: [0, 0, 0, 0],
                ModeDeltas: [0, 0, 0, 0]),
            log2TokenPartitionCount,
            1 << log2TokenPartitionCount,
            new Vp8QuantizationHeader(
                YAcQuantizerIndex: 0,
                YDcDelta: 0,
                Y2DcDelta: 0,
                Y2AcDelta: 0,
                UvDcDelta: 0,
                UvAcDelta: 0),
            RefreshEntropyProbabilities: false,
            CoefficientProbabilityUpdates: [],
            MbNoCoeffSkip: false,
            ProbSkipFalse: null);
    }
}
