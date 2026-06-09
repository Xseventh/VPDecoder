#if VPDECODER_PROFILE
namespace VPDecoder;

using System.Diagnostics;

internal readonly record struct Vp9PerfCounterSnapshot(
    long HeaderParseTicks,
    long CompressedHeaderParseTicks,
    long TileLayoutParseTicks,
    long KeyFrameReconstructionTicks,
    long InterFrameReconstructionTicks,
    long InterModeInfoTicks,
    long InterPredictionTicks,
    long InterResidualTicks,
    long InterIntraBlockTicks,
    long InterIntraResidualReadTicks,
    long InterIntraReconstructionTicks,
    long MotionCopyWholePixelTicks,
    long MotionCopyHorizontalTicks,
    long MotionCopyVerticalTicks,
    long MotionCopyTwoDimensionalTicks,
    long MotionCopyClampedTicks,
    long MotionAverageWholePixelTicks,
    long MotionAverageFilteredTicks,
    long LoopFilterTicks,
    long PreviousMotionVectorTicks,
    long ColorConversionTicks,
    long AlphaMergeTicks)
{
    public long AccountedTicks =>
        HeaderParseTicks +
        CompressedHeaderParseTicks +
        TileLayoutParseTicks +
        KeyFrameReconstructionTicks +
        InterFrameReconstructionTicks +
        LoopFilterTicks +
        PreviousMotionVectorTicks +
        ColorConversionTicks +
        AlphaMergeTicks;

    public static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}

internal static class Vp9PerfCounters
{
    private static long _headerParseTicks;
    private static long _compressedHeaderParseTicks;
    private static long _tileLayoutParseTicks;
    private static long _keyFrameReconstructionTicks;
    private static long _interFrameReconstructionTicks;
    private static long _interModeInfoTicks;
    private static long _interPredictionTicks;
    private static long _interResidualTicks;
    private static long _interIntraBlockTicks;
    private static long _interIntraResidualReadTicks;
    private static long _interIntraReconstructionTicks;
    private static long _motionCopyWholePixelTicks;
    private static long _motionCopyHorizontalTicks;
    private static long _motionCopyVerticalTicks;
    private static long _motionCopyTwoDimensionalTicks;
    private static long _motionCopyClampedTicks;
    private static long _motionAverageWholePixelTicks;
    private static long _motionAverageFilteredTicks;
    private static long _loopFilterTicks;
    private static long _previousMotionVectorTicks;
    private static long _colorConversionTicks;
    private static long _alphaMergeTicks;

    public static long Start()
    {
        return Stopwatch.GetTimestamp();
    }

    public static void Reset()
    {
        _headerParseTicks = 0;
        _compressedHeaderParseTicks = 0;
        _tileLayoutParseTicks = 0;
        _keyFrameReconstructionTicks = 0;
        _interFrameReconstructionTicks = 0;
        _interModeInfoTicks = 0;
        _interPredictionTicks = 0;
        _interResidualTicks = 0;
        _interIntraBlockTicks = 0;
        _interIntraResidualReadTicks = 0;
        _interIntraReconstructionTicks = 0;
        _motionCopyWholePixelTicks = 0;
        _motionCopyHorizontalTicks = 0;
        _motionCopyVerticalTicks = 0;
        _motionCopyTwoDimensionalTicks = 0;
        _motionCopyClampedTicks = 0;
        _motionAverageWholePixelTicks = 0;
        _motionAverageFilteredTicks = 0;
        _loopFilterTicks = 0;
        _previousMotionVectorTicks = 0;
        _colorConversionTicks = 0;
        _alphaMergeTicks = 0;
    }

    public static Vp9PerfCounterSnapshot Snapshot()
    {
        return new Vp9PerfCounterSnapshot(
            _headerParseTicks,
            _compressedHeaderParseTicks,
            _tileLayoutParseTicks,
            _keyFrameReconstructionTicks,
            _interFrameReconstructionTicks,
            _interModeInfoTicks,
            _interPredictionTicks,
            _interResidualTicks,
            _interIntraBlockTicks,
            _interIntraResidualReadTicks,
            _interIntraReconstructionTicks,
            _motionCopyWholePixelTicks,
            _motionCopyHorizontalTicks,
            _motionCopyVerticalTicks,
            _motionCopyTwoDimensionalTicks,
            _motionCopyClampedTicks,
            _motionAverageWholePixelTicks,
            _motionAverageFilteredTicks,
            _loopFilterTicks,
            _previousMotionVectorTicks,
            _colorConversionTicks,
            _alphaMergeTicks);
    }

    public static void AddHeaderParse(long start) => _headerParseTicks += Stopwatch.GetTimestamp() - start;

    public static void AddCompressedHeaderParse(long start) => _compressedHeaderParseTicks += Stopwatch.GetTimestamp() - start;

    public static void AddTileLayoutParse(long start) => _tileLayoutParseTicks += Stopwatch.GetTimestamp() - start;

    public static void AddKeyFrameReconstruction(long start) => _keyFrameReconstructionTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterFrameReconstruction(long start) => _interFrameReconstructionTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterModeInfo(long start) => _interModeInfoTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterPrediction(long start) => _interPredictionTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterResidual(long start) => _interResidualTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterIntraBlock(long start) => _interIntraBlockTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterIntraResidualRead(long start) => _interIntraResidualReadTicks += Stopwatch.GetTimestamp() - start;

    public static void AddInterIntraReconstruction(long start) => _interIntraReconstructionTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionCopyWholePixel(long start) => _motionCopyWholePixelTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionCopyHorizontal(long start) => _motionCopyHorizontalTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionCopyVertical(long start) => _motionCopyVerticalTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionCopyTwoDimensional(long start) => _motionCopyTwoDimensionalTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionCopyClamped(long start) => _motionCopyClampedTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionAverageWholePixel(long start) => _motionAverageWholePixelTicks += Stopwatch.GetTimestamp() - start;

    public static void AddMotionAverageFiltered(long start) => _motionAverageFilteredTicks += Stopwatch.GetTimestamp() - start;

    public static void AddLoopFilter(long start) => _loopFilterTicks += Stopwatch.GetTimestamp() - start;

    public static void AddPreviousMotionVector(long start) => _previousMotionVectorTicks += Stopwatch.GetTimestamp() - start;

    public static void AddColorConversion(long start) => _colorConversionTicks += Stopwatch.GetTimestamp() - start;

    public static void AddAlphaMerge(long start) => _alphaMergeTicks += Stopwatch.GetTimestamp() - start;
}
#endif
