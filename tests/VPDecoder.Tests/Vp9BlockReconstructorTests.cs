namespace VPDecoder.Tests;

public sealed class Vp9BlockReconstructorTests
{
    [Fact]
    public void ReconstructDcOnlyGroup_ForTx32AcBlock_AppliesInverseTransformAfterDcPrediction()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(32, 32);
        var geometry = new Vp9TileGeometry(
            TileRow: 0,
            TileColumn: 0,
            MiRowStart: 0,
            MiRowEnd: 4,
            MiColumnStart: 0,
            MiColumnEnd: 4,
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: 0));
        var modeInfo = new Vp9ModeInfoProbe(
            TileIndex: 0,
            MiRow: 0,
            MiColumn: 0,
            Vp9BlockSize.Block32X32,
            PartitionPath: [Vp9PartitionType.None],
            Skip: false,
            SkipContext: 0,
            Vp9TransformSize.Tx32X32,
            TransformSizeContext: 0,
            Vp9PredictionMode.Dc,
            Vp9PredictionMode.Dc,
            YSubModes: []);
        var coefficients = new int[1024];
        coefficients[0] = 16625;
        coefficients[1] = 420;
        coefficients[32] = -315;
        coefficients[33] = 210;
        var group = new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            Vp9BlockSize.Block32X32,
            Vp9TransformSize.Tx32X32,
            [
                new Vp9CoefficientBlockProbe(
                    TileIndex: 0,
                    Vp9TransformSize.Tx32X32,
                    Vp9TransformType.DctDct,
                    Row4: 0,
                    Column4: 0,
                    PlaneType: 0,
                    ReferenceType: 0,
                    InitialCoefficientContext: 0,
                    Eob: 4,
                    NonZeroCount: 4,
                    FirstNonZeroRasterIndex: 0,
                    LastNonZeroRasterIndex: 33,
                    coefficients,
                    CoefficientsSha256: "synthetic")
            ]);

        Vp9BlockReconstructor.ReconstructDcOnlyGroup(frameBuffer, geometry, modeInfo, group, plane: 0);

        var yPlane = frameBuffer.Pixels.AsSpan(frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length).ToArray();
        Assert.Equal("e88985af3f6e6f538a58eca3ee27c51f903a5c6dcbf96540d4a372f0509062ef", Hash(yPlane));
        Assert.True(yPlane.Distinct().Count() > 1);
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
