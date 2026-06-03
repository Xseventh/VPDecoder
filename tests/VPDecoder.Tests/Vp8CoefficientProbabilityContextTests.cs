namespace VPDecoder.Tests;

public sealed class Vp8CoefficientProbabilityContextTests
{
    [Fact]
    public void CreateDefault_LoadsLibvpxDefaultCoefficientProbabilities()
    {
        var context = Vp8CoefficientProbabilityContext.CreateDefault();

        Assert.Equal(4 * 8 * 3 * 11, Vp8CoefficientProbabilityContext.Count);
        Assert.Equal(128, context.GetProbability(0, 0, 0, 0));
        Assert.Equal(253, context.GetProbability(0, 1, 0, 0));
        Assert.Equal(198, context.GetProbability(1, 0, 0, 0));
        Assert.Equal(253, context.GetProbability(2, 0, 0, 0));
        Assert.Equal(202, context.GetProbability(3, 0, 0, 0));
    }

    [Fact]
    public void Create_AppliesHeaderCoefficientProbabilityUpdates()
    {
        var syntaxHeader = CreateSyntaxHeader(
        [
            new Vp8CoefficientProbabilityUpdate(
                BlockType: 0,
                CoefficientBand: 1,
                PreviousCoefficientContext: 0,
                EntropyNode: 0,
                Probability: 77)
        ]);

        var context = Vp8CoefficientProbabilityContext.Create(syntaxHeader);

        Assert.Equal(77, context.GetProbability(0, 1, 0, 0));
        Assert.Equal(128, context.GetProbability(0, 0, 0, 0));
    }

    private static Vp8KeyFrameSyntaxHeader CreateSyntaxHeader(
        IReadOnlyList<Vp8CoefficientProbabilityUpdate> coefficientProbabilityUpdates)
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
            Log2TokenPartitionCount: 0,
            TokenPartitionCount: 1,
            new Vp8QuantizationHeader(
                YAcQuantizerIndex: 0,
                YDcDelta: 0,
                Y2DcDelta: 0,
                Y2AcDelta: 0,
                UvDcDelta: 0,
                UvAcDelta: 0),
            RefreshEntropyProbabilities: false,
            coefficientProbabilityUpdates,
            MbNoCoeffSkip: false,
            ProbSkipFalse: null);
    }
}
