using System.Diagnostics;
using VPDecoder;

if (args.Length is > 0 && (args[0] is "-h" or "--help" or "help"))
{
    PrintUsage();
    return 0;
}

var root = args.Length > 0 ? args[0] : ".";
var stream = args.Length > 1 ? args[1].ToLowerInvariant() : "both";
var format = args.Length > 2 ? ParseFormat(args[2]) : Vp9OutputPixelFormat.Yuv420;
var iterations = args.Length > 3 ? int.Parse(args[3]) : 3;
var frames = args.Length > 4 ? int.Parse(args[4]) : 1;
var expectedWidth = args.Length > 5 ? int.Parse(args[5]) : 2656;
var expectedHeight = args.Length > 6 ? int.Parse(args[6]) : 1352;

if (iterations <= 0)
{
    throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iteration count must be positive.");
}

if (frames <= 0)
{
    throw new ArgumentOutOfRangeException(nameof(frames), frames, "Frame count must be positive.");
}

if (stream is not ("color" or "alpha" or "both" or "merged"))
{
    throw new ArgumentException("Stream must be color, alpha, both, or merged.", nameof(stream));
}

var colorPackets = stream is "color" or "both" or "merged"
    ? LoadPackets(root, "color", frames)
    : [];
var alphaPackets = stream is "alpha" or "both" or "merged"
    ? LoadPackets(root, "alpha", frames)
    : [];
var options = new Vp9DecodeOptions(expectedWidth, expectedHeight, format);

var warmupChecksum = RunOnce(colorPackets, alphaPackets, options, stream == "merged");
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();

var elapsed = new double[iterations];
var allocatedBytes = new long[iterations];
#if VPDECODER_PROFILE
var profileSnapshots = new Vp9PerfCounterSnapshot[iterations];
#endif
var checksum = 0UL;
for (var iteration = 0; iteration < iterations; iteration++)
{
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();

#if VPDECODER_PROFILE
    Vp9PerfCounters.Reset();
#endif
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var sw = Stopwatch.StartNew();
    checksum = RunOnce(colorPackets, alphaPackets, options, stream == "merged");
    sw.Stop();

    elapsed[iteration] = sw.Elapsed.TotalMilliseconds;
    allocatedBytes[iteration] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
#if VPDECODER_PROFILE
    profileSnapshots[iteration] = Vp9PerfCounters.Snapshot();
#endif
}

Array.Sort(elapsed);
Array.Sort(allocatedBytes);

var packetCount = colorPackets.Count + alphaPackets.Count;
var averageMs = elapsed.Average();
Console.WriteLine(
    $"vpdecoder stream={stream} format={format} frames={frames} packets={packetCount} iterations={iterations} warmupChecksum={warmupChecksum} checksum={checksum} avgMs={averageMs:F3} medianMs={elapsed[elapsed.Length / 2]:F3} minMs={elapsed[0]:F3} maxMs={elapsed[^1]:F3} packetsPerSec={packetCount / (averageMs / 1000.0):F2} avgAllocMb={allocatedBytes.Average() / (1024.0 * 1024.0):F1} medianAllocMb={allocatedBytes[allocatedBytes.Length / 2] / (1024.0 * 1024.0):F1} minAllocMb={allocatedBytes[0] / (1024.0 * 1024.0):F1} maxAllocMb={allocatedBytes[^1] / (1024.0 * 1024.0):F1}");
#if VPDECODER_PROFILE
PrintProfile(profileSnapshots, averageMs);
#endif

return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project tools/VPDecoder.Bench -c Release -- <packet-root> <stream> <format> <iterations> <frames> [expected-width] [expected-height]");
    Console.WriteLine("  stream: color | alpha | both | merged");
    Console.WriteLine("  format: yuv420 | bgra8888 | rgba8888");
    Console.WriteLine("  packet names: frame-000-color.vp9, frame-000-alpha.vp9, ...");
}

static Vp9OutputPixelFormat ParseFormat(string value)
{
    return value.ToLowerInvariant() switch
    {
        "bgra" or "bgra8888" => Vp9OutputPixelFormat.Bgra8888,
        "rgba" or "rgba8888" => Vp9OutputPixelFormat.Rgba8888,
        "yuv" or "yuv420" => Vp9OutputPixelFormat.Yuv420,
        _ => throw new ArgumentException($"Unknown format '{value}'.", nameof(value))
    };
}

static List<byte[]> LoadPackets(string root, string suffix, int frames)
{
    var packets = new List<byte[]>(frames);
    for (var frame = 0; frame < frames; frame++)
    {
        var path = Path.Combine(root, $"frame-{frame:000}-{suffix}.vp9");
        packets.Add(File.ReadAllBytes(path));
    }

    return packets;
}

static ulong RunOnce(
    IReadOnlyList<byte[]> colorPackets,
    IReadOnlyList<byte[]> alphaPackets,
    Vp9DecodeOptions options,
    bool merged)
{
    var checksum = 1469598103934665603UL;
    if (merged)
    {
        return RunMerged(colorPackets, alphaPackets, options, checksum);
    }

    var colorDecoder = colorPackets.Count > 0 ? new RawVp9Decoder() : null;
    var alphaDecoder = alphaPackets.Count > 0 ? new RawVp9Decoder() : null;
    var frameCount = Math.Max(colorPackets.Count, alphaPackets.Count);
    for (var frame = 0; frame < frameCount; frame++)
    {
        if (frame < colorPackets.Count)
        {
            checksum = DecodeOne(colorDecoder!, colorPackets[frame], options, checksum, "color", frame);
        }

        if (frame < alphaPackets.Count)
        {
            checksum = DecodeOne(alphaDecoder!, alphaPackets[frame], options, checksum, "alpha", frame);
        }
    }

    return checksum;
}

static ulong RunMerged(
    IReadOnlyList<byte[]> colorPackets,
    IReadOnlyList<byte[]> alphaPackets,
    Vp9DecodeOptions options,
    ulong checksum)
{
    if (colorPackets.Count != alphaPackets.Count)
    {
        throw new InvalidOperationException("Merged benchmark requires matching color and alpha packet counts.");
    }

    var decoder = new RawVp9Decoder();
    for (var frame = 0; frame < colorPackets.Count; frame++)
    {
        var result = decoder.DecodeFrameWithAlpha(colorPackets[frame], alphaPackets[frame], options);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"merged frame {frame}: {result.Diagnostic?.Code}: {result.Diagnostic?.Message}");
        }

        checksum = FoldFrameChecksum(result.Frame, checksum);
    }

    return checksum;
}

static ulong DecodeOne(
    RawVp9Decoder decoder,
    byte[] packet,
    Vp9DecodeOptions options,
    ulong checksum,
    string stream,
    int frame)
{
    var result = decoder.DecodeFrame(packet, options);
    if (!result.Succeeded)
    {
        throw new InvalidOperationException($"{stream} frame {frame}: {result.Diagnostic?.Code}: {result.Diagnostic?.Message}");
    }

    return FoldFrameChecksum(result.Frame, checksum);
}

static ulong FoldFrameChecksum(Vp9DecodedFrame? frame, ulong checksum)
{
    if (frame is null)
    {
        return checksum;
    }

    checksum ^= (uint)frame.Width;
    checksum *= 1099511628211UL;
    checksum ^= (uint)frame.Height;
    checksum *= 1099511628211UL;
    checksum ^= (uint)frame.Pixels.Length;
    checksum *= 1099511628211UL;
    if (frame.Pixels.Length > 0)
    {
        checksum ^= frame.Pixels[0];
        checksum *= 1099511628211UL;
        checksum ^= frame.Pixels[^1];
        checksum *= 1099511628211UL;
    }

    return checksum;
}

#if VPDECODER_PROFILE
static void PrintProfile(Vp9PerfCounterSnapshot[] snapshots, double averageMs)
{
    var accountedMs = AverageMs(snapshots, static snapshot => snapshot.AccountedTicks);
    Console.WriteLine(
        "vpdecoder-profile " +
        $"accountedMs={accountedMs:F3} accountedPct={Percent(accountedMs, averageMs):F1} " +
        FormatStage("header", AverageMs(snapshots, static snapshot => snapshot.HeaderParseTicks), averageMs) + " " +
        FormatStage("compressedHeader", AverageMs(snapshots, static snapshot => snapshot.CompressedHeaderParseTicks), averageMs) + " " +
        FormatStage("tileLayout", AverageMs(snapshots, static snapshot => snapshot.TileLayoutParseTicks), averageMs) + " " +
        FormatStage("keyRecon", AverageMs(snapshots, static snapshot => snapshot.KeyFrameReconstructionTicks), averageMs) + " " +
        FormatStage("interRecon", AverageMs(snapshots, static snapshot => snapshot.InterFrameReconstructionTicks), averageMs) + " " +
        FormatNestedStage("interModeInfo", AverageMs(snapshots, static snapshot => snapshot.InterModeInfoTicks), AverageMs(snapshots, static snapshot => snapshot.InterFrameReconstructionTicks)) + " " +
        FormatNestedStage("interPrediction", AverageMs(snapshots, static snapshot => snapshot.InterPredictionTicks), AverageMs(snapshots, static snapshot => snapshot.InterFrameReconstructionTicks)) + " " +
        FormatNestedStage("interResidual", AverageMs(snapshots, static snapshot => snapshot.InterResidualTicks), AverageMs(snapshots, static snapshot => snapshot.InterFrameReconstructionTicks)) + " " +
        FormatNestedStage("interIntraBlock", AverageMs(snapshots, static snapshot => snapshot.InterIntraBlockTicks), AverageMs(snapshots, static snapshot => snapshot.InterFrameReconstructionTicks)) + " " +
        FormatStage("loopFilter", AverageMs(snapshots, static snapshot => snapshot.LoopFilterTicks), averageMs) + " " +
        FormatStage("previousMv", AverageMs(snapshots, static snapshot => snapshot.PreviousMotionVectorTicks), averageMs) + " " +
        FormatStage("colorConversion", AverageMs(snapshots, static snapshot => snapshot.ColorConversionTicks), averageMs) + " " +
        FormatStage("alphaMerge", AverageMs(snapshots, static snapshot => snapshot.AlphaMergeTicks), averageMs));
}

static double AverageMs(Vp9PerfCounterSnapshot[] snapshots, Func<Vp9PerfCounterSnapshot, long> selector)
{
    var ticks = 0L;
    foreach (var snapshot in snapshots)
    {
        ticks += selector(snapshot);
    }

    return Vp9PerfCounterSnapshot.ToMilliseconds(ticks) / snapshots.Length;
}

static string FormatStage(string name, double stageMs, double averageMs)
{
    return $"{name}Ms={stageMs:F3} {name}Pct={Percent(stageMs, averageMs):F1}";
}

static string FormatNestedStage(string name, double stageMs, double parentMs)
{
    return $"{name}Ms={stageMs:F3} {name}OfInterPct={Percent(stageMs, parentMs):F1}";
}

static double Percent(double numerator, double denominator)
{
    return denominator <= 0 ? 0 : numerator * 100.0 / denominator;
}
#endif
