using VPDecoder;

return VpDecoderCli.Run(args);

internal static class VpDecoderCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var inputPath = GetValue(args, "--input");
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.Error.WriteLine("Missing required --input path.");
            PrintUsage();
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file does not exist: {inputPath}");
            return 1;
        }

        var alphaPath = GetValue(args, "--alpha");
        if (!string.IsNullOrWhiteSpace(alphaPath) && !File.Exists(alphaPath))
        {
            Console.Error.WriteLine($"Alpha input file does not exist: {alphaPath}");
            return 1;
        }

        if (!TryReadNullableInt(args, "--width", out var width) ||
            !TryReadNullableInt(args, "--height", out var height))
        {
            return 1;
        }

        if (!TryReadFormat(args, out var format))
        {
            return 1;
        }

        var outputPath = GetValue(args, "--out");
        var packet = File.ReadAllBytes(inputPath);
        var decoder = new RawVp9Decoder();
        var options = new Vp9DecodeOptions(width, height, format);
        var result = string.IsNullOrWhiteSpace(alphaPath)
            ? decoder.DecodeFrame(packet, options)
            : decoder.DecodeFrameWithAlpha(packet, File.ReadAllBytes(alphaPath), options);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"{result.Diagnostic?.Code}: {result.Diagnostic?.Message}");
            if (result.Header is not null)
            {
                Console.Error.WriteLine($"Header: {result.Header.Width}x{result.Header.Height}, profile {result.Header.Profile}, tiles {result.Header.TileInfo.TileColumns}x{result.Header.TileInfo.TileRows}");
            }

            return 2;
        }

        if (result.NoDisplayFrame)
        {
            Console.WriteLine($"Decoded no-display VP9 frame, status {result.Status}.");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            File.WriteAllBytes(outputPath, result.Frame!.Pixels);
        }

        Console.WriteLine($"Decoded {result.Frame!.Width}x{result.Frame.Height} {result.Frame.PixelFormat}, {result.Frame.Pixels.Length} bytes.");
        return 0;
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Contains(name, StringComparer.Ordinal);
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool TryReadNullableInt(string[] args, string name, out int? value)
    {
        value = null;
        var text = GetValue(args, name);
        if (text is null)
        {
            return true;
        }

        if (int.TryParse(text, out var parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        Console.Error.WriteLine($"{name} must be a positive integer.");
        return false;
    }

    private static bool TryReadFormat(string[] args, out Vp9OutputPixelFormat format)
    {
        format = Vp9OutputPixelFormat.Bgra8888;
        var text = GetValue(args, "--format");
        if (text is null)
        {
            return true;
        }

        if (text.Equals("bgra", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("bgra8888", StringComparison.OrdinalIgnoreCase))
        {
            format = Vp9OutputPixelFormat.Bgra8888;
            return true;
        }

        if (text.Equals("rgba", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("rgba8888", StringComparison.OrdinalIgnoreCase))
        {
            format = Vp9OutputPixelFormat.Rgba8888;
            return true;
        }

        if (text.Equals("yuv", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("yuv420", StringComparison.OrdinalIgnoreCase))
        {
            format = Vp9OutputPixelFormat.Yuv420;
            return true;
        }

        Console.Error.WriteLine("--format must be bgra, rgba, or yuv420.");
        return false;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: VPDecoder.Cli --input frame.vp9 [--alpha alpha.vp9] [--width 2656] [--height 1352] [--format bgra|rgba|yuv420] [--out frame.raw]");
    }
}
