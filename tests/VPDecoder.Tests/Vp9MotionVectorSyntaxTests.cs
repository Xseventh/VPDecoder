namespace VPDecoder.Tests;

public sealed class Vp9MotionVectorSyntaxTests
{
    [Theory]
    [InlineData(new byte[] { 0x00, 0x00 }, (int)Vp9MotionVectorJoint.Zero)]
    [InlineData(new byte[] { 0x10, 0x00 }, (int)Vp9MotionVectorJoint.HorizontalNonZeroVerticalZero)]
    [InlineData(new byte[] { 0x2c, 0x00 }, (int)Vp9MotionVectorJoint.HorizontalZeroVerticalNonZero)]
    [InlineData(new byte[] { 0x4b, 0x80 }, (int)Vp9MotionVectorJoint.HorizontalNonZeroVerticalNonZero)]
    public void ReadJoint_ReadsLibvpxJointTree(byte[] payload, int expected)
    {
        var reader = new Vp9BoolReader(payload);

        var joint = Vp9MotionVectorSyntax.ReadJoint(
            ref reader,
            Vp9FrameContext.CreateDefault().MotionVectorProbabilities);

        Assert.Equal((Vp9MotionVectorJoint)expected, joint);
        Assert.False(reader.HasError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadMotionVector_WhenJointIsZero_ReturnsReferenceVector(bool allowHighPrecision)
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);
        var reference = new Vp9MotionVector(Row: 12, Column: -7);

        var motionVector = Vp9MotionVectorSyntax.ReadMotionVector(
            ref reader,
            Vp9FrameContext.CreateDefault().MotionVectorProbabilities,
            reference,
            allowHighPrecision);

        Assert.Equal(reference, motionVector);
        Assert.False(reader.HasError);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(63, -63, true)]
    [InlineData(64, 0, false)]
    [InlineData(0, -64, false)]
    public void UseHighPrecision_MatchesLibvpxReferenceThreshold(int row, int column, bool expected)
    {
        Assert.Equal(expected, Vp9MotionVectorSyntax.UseHighPrecision(new Vp9MotionVector(row, column)));
    }

    [Fact]
    public void TryReadSupportedInterBlock_WhenPredictionModeIsNewMv_ReturnsUnsupportedDiagnostic()
    {
        var reader = new Vp9BoolReader([0x0c, 0x24, 0x00]);
        var frameHeader = Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateOrdinaryInterFramePacket());
        var compressedHeader = new Vp9CompressedHeader(
            Vp9TransformMode.Only4X4,
            Vp9FrameContext.CreateDefault(),
            TxProbabilityUpdateCount: 0,
            CoefficientProbabilityUpdateCount: 0,
            SkipProbabilityUpdateCount: 0,
            ReferenceMode: Vp9ReferenceMode.Single);
        var contexts = new Vp9InterModeInfoContexts(
            Skip: 0,
            IntraInter: 0,
            SingleReference0: 0,
            SingleReference1: 0,
            InterMode: 0);

        Assert.False(Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
            ref reader,
            frameHeader,
            compressedHeader,
            Vp9BlockSize.Block64X64,
            contexts,
            out var probe,
            out var diagnostic));

        Assert.Null(probe);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("NEWMV", diagnostic?.Message, StringComparison.Ordinal);
    }
}
