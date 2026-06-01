namespace VPDecoder;

public static class Vp9FrameHeaderParser
{
    private const int Vp9FrameMarker = 2;
    private const int SyncCode0 = 0x49;
    private const int SyncCode1 = 0x83;
    private const int SyncCode2 = 0x42;

    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        out Vp9FrameHeader? header,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        header = null;
        diagnostic = null;

        try
        {
            header = Parse(packet);
            return true;
        }
        catch (Vp9HeaderParseException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static Vp9FrameHeader Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("VP9 packet is empty."));
        }

        var reader = new Vp9BitReader(packet);
        var frameMarker = reader.ReadLiteral(2);
        if (frameMarker != Vp9FrameMarker)
        {
            throw new Vp9HeaderParseException(
                Vp9DecodeDiagnostic.InvalidPacket($"Invalid VP9 frame marker: {frameMarker}."));
        }

        var profile = ReadProfile(ref reader);
        var showExistingFrame = reader.ReadBit();
        if (showExistingFrame)
        {
            var existingFrameIndex = reader.ReadLiteral(3);
            return new Vp9FrameHeader(
                packet.Length,
                reader.BytesRead,
                frameMarker,
                profile,
                true,
                existingFrameIndex,
                Vp9FrameType.InterFrame,
                true,
                false,
                true,
                8,
                Vp9ColorSpace.Unknown,
                Vp9ColorRange.Studio,
                1,
                1,
                0,
                0,
                0,
                0,
                false,
                true,
                0,
                new Vp9LoopFilterHeader(0, 0, false, false, [0, 0, 0, 0], [0, 0]),
                new Vp9QuantizationHeader(0, 0, 0, 0),
                new Vp9SegmentationHeader(false, false, false, false, false, [], []),
                new Vp9TileInfo(0, 0, 0, 0, 0, 0, 0),
                0);
        }

        var frameType = reader.ReadBit() ? Vp9FrameType.InterFrame : Vp9FrameType.KeyFrame;
        var showFrame = reader.ReadBit();
        var errorResilientMode = reader.ReadBit();

        if (frameType != Vp9FrameType.KeyFrame)
        {
            throw new Vp9HeaderParseException(
                Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 inter-frame header parsing is not implemented yet."));
        }

        var syncCodeValid =
            reader.ReadLiteral(8) == SyncCode0 &&
            reader.ReadLiteral(8) == SyncCode1 &&
            reader.ReadLiteral(8) == SyncCode2;
        if (!syncCodeValid)
        {
            throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("Invalid VP9 key-frame sync code."));
        }

        var color = ReadBitDepthColorSpaceAndSampling(ref reader, profile);
        var width = reader.ReadLiteral(16) + 1;
        var height = reader.ReadLiteral(16) + 1;
        if (width <= 0 || height <= 0)
        {
            throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("Invalid VP9 frame dimensions."));
        }

        var renderWidth = width;
        var renderHeight = height;
        if (reader.ReadBit())
        {
            renderWidth = reader.ReadLiteral(16) + 1;
            renderHeight = reader.ReadLiteral(16) + 1;
        }

        bool refreshFrameContext;
        bool frameParallelDecodingMode;
        if (errorResilientMode)
        {
            refreshFrameContext = false;
            frameParallelDecodingMode = true;
        }
        else
        {
            refreshFrameContext = reader.ReadBit();
            frameParallelDecodingMode = reader.ReadBit();
        }

        var frameContextIndex = reader.ReadLiteral(2);
        var loopFilter = ReadLoopFilter(ref reader);
        var quantization = ReadQuantization(ref reader);
        var segmentation = ReadSegmentation(ref reader);
        var tileInfo = ReadTileInfo(ref reader, width, height);
        var firstPartitionSize = reader.ReadLiteral(16);

        return new Vp9FrameHeader(
            packet.Length,
            reader.BytesRead,
            frameMarker,
            profile,
            false,
            null,
            frameType,
            showFrame,
            errorResilientMode,
            syncCodeValid,
            color.BitDepth,
            color.ColorSpace,
            color.ColorRange,
            color.SubsamplingX,
            color.SubsamplingY,
            width,
            height,
            renderWidth,
            renderHeight,
            refreshFrameContext,
            frameParallelDecodingMode,
            frameContextIndex,
            loopFilter,
            quantization,
            segmentation,
            tileInfo,
            firstPartitionSize);
    }

    private static int ReadProfile(ref Vp9BitReader reader)
    {
        var profile = reader.ReadBit() ? 1 : 0;
        if (reader.ReadBit())
        {
            profile |= 2;
        }

        if (profile > 2 && reader.ReadBit())
        {
            profile += 1;
        }

        return profile;
    }

    private static Vp9ColorHeader ReadBitDepthColorSpaceAndSampling(ref Vp9BitReader reader, int profile)
    {
        var bitDepth = profile >= 2
            ? reader.ReadBit() ? 12 : 10
            : 8;

        var colorSpace = (Vp9ColorSpace)reader.ReadLiteral(3);
        if (colorSpace == Vp9ColorSpace.Srgb)
        {
            if (profile is 1 or 3)
            {
                var reservedBit = reader.ReadBit();
                if (reservedBit)
                {
                    throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("VP9 reserved color bit is set."));
                }

                return new Vp9ColorHeader(bitDepth, colorSpace, Vp9ColorRange.Full, 0, 0);
            }

            throw new Vp9HeaderParseException(
                Vp9DecodeDiagnostic.UnsupportedChromaSubsampling("VP9 sRGB color space is invalid for profile 0 or 2."));
        }

        var colorRange = reader.ReadBit() ? Vp9ColorRange.Full : Vp9ColorRange.Studio;
        if (profile is 1 or 3)
        {
            var subsamplingX = reader.ReadBit() ? 1 : 0;
            var subsamplingY = reader.ReadBit() ? 1 : 0;
            if (subsamplingX == 1 && subsamplingY == 1)
            {
                throw new Vp9HeaderParseException(
                    Vp9DecodeDiagnostic.UnsupportedChromaSubsampling("VP9 profile 1 or 3 cannot use 4:2:0 chroma."));
            }

            var reservedBit = reader.ReadBit();
            if (reservedBit)
            {
                throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("VP9 reserved color bit is set."));
            }

            return new Vp9ColorHeader(bitDepth, colorSpace, colorRange, subsamplingX, subsamplingY);
        }

        return new Vp9ColorHeader(bitDepth, colorSpace, colorRange, 1, 1);
    }

    private static Vp9LoopFilterHeader ReadLoopFilter(ref Vp9BitReader reader)
    {
        var filterLevel = reader.ReadLiteral(6);
        var sharpnessLevel = reader.ReadLiteral(3);
        var refDeltas = new[] { 0, 0, 0, 0 };
        var modeDeltas = new[] { 0, 0 };

        var modeRefDeltaEnabled = reader.ReadBit();
        var modeRefDeltaUpdate = false;
        if (modeRefDeltaEnabled)
        {
            modeRefDeltaUpdate = reader.ReadBit();
            if (modeRefDeltaUpdate)
            {
                for (var i = 0; i < refDeltas.Length; i++)
                {
                    if (reader.ReadBit())
                    {
                        refDeltas[i] = reader.ReadSignedLiteral(6);
                    }
                }

                for (var i = 0; i < modeDeltas.Length; i++)
                {
                    if (reader.ReadBit())
                    {
                        modeDeltas[i] = reader.ReadSignedLiteral(6);
                    }
                }
            }
        }

        return new Vp9LoopFilterHeader(
            filterLevel,
            sharpnessLevel,
            modeRefDeltaEnabled,
            modeRefDeltaUpdate,
            refDeltas,
            modeDeltas);
    }

    private static Vp9QuantizationHeader ReadQuantization(ref Vp9BitReader reader)
    {
        return new Vp9QuantizationHeader(
            reader.ReadLiteral(8),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader));
    }

    private static int ReadDeltaQ(ref Vp9BitReader reader)
    {
        return reader.ReadBit() ? reader.ReadSignedLiteral(4) : 0;
    }

    private static Vp9SegmentationHeader ReadSegmentation(ref Vp9BitReader reader)
    {
        var enabled = reader.ReadBit();
        if (!enabled)
        {
            return new Vp9SegmentationHeader(false, false, false, false, false, [], []);
        }

        var treeProbabilities = new byte?[7];
        var predictionProbabilities = new byte?[3];
        var updateMap = reader.ReadBit();
        var temporalUpdate = false;
        if (updateMap)
        {
            for (var i = 0; i < treeProbabilities.Length; i++)
            {
                treeProbabilities[i] = reader.ReadBit() ? (byte)reader.ReadLiteral(8) : null;
            }

            temporalUpdate = reader.ReadBit();
            if (temporalUpdate)
            {
                for (var i = 0; i < predictionProbabilities.Length; i++)
                {
                    predictionProbabilities[i] = reader.ReadBit() ? (byte)reader.ReadLiteral(8) : null;
                }
            }
        }

        var updateData = reader.ReadBit();
        var absoluteData = false;
        if (updateData)
        {
            absoluteData = reader.ReadBit();
            for (var segment = 0; segment < 8; segment++)
            {
                for (var feature = 0; feature < 4; feature++)
                {
                    var featureEnabled = reader.ReadBit();
                    if (!featureEnabled)
                    {
                        continue;
                    }

                    var max = feature switch
                    {
                        0 => 255,
                        1 => 63,
                        2 => 3,
                        3 => 0,
                        _ => throw new InvalidOperationException("Unexpected VP9 segmentation feature index.")
                    };
                    _ = DecodeUnsignedMax(ref reader, max);
                    if (feature is 0 or 1)
                    {
                        _ = reader.ReadBit();
                    }
                }
            }
        }

        return new Vp9SegmentationHeader(
            true,
            updateMap,
            temporalUpdate,
            updateData,
            absoluteData,
            treeProbabilities,
            predictionProbabilities);
    }

    private static Vp9TileInfo ReadTileInfo(ref Vp9BitReader reader, int width, int height)
    {
        var miColumns = AlignPowerOfTwo(width, 3) >> 3;
        var miRows = AlignPowerOfTwo(height, 3) >> 3;
        var superblockColumns = AlignPowerOfTwo(miColumns, 3) >> 3;

        var minLog2TileColumns = 0;
        while ((64 << minLog2TileColumns) < superblockColumns)
        {
            minLog2TileColumns++;
        }

        var maxLog2TileColumns = 1;
        while ((superblockColumns >> maxLog2TileColumns) >= 4)
        {
            maxLog2TileColumns++;
        }

        var log2TileColumns = minLog2TileColumns;
        for (var maxOnes = maxLog2TileColumns - minLog2TileColumns; maxOnes > 0 && reader.ReadBit(); maxOnes--)
        {
            log2TileColumns++;
        }

        var log2TileRows = reader.ReadBit() ? 1 : 0;
        if (log2TileRows != 0 && reader.ReadBit())
        {
            log2TileRows++;
        }

        return new Vp9TileInfo(
            miColumns,
            miRows,
            superblockColumns,
            minLog2TileColumns,
            maxLog2TileColumns,
            log2TileColumns,
            log2TileRows);
    }

    private static int DecodeUnsignedMax(ref Vp9BitReader reader, int max)
    {
        if (max == 0)
        {
            return 0;
        }

        var bits = 0;
        for (var value = max; value > 0; value >>= 1)
        {
            bits++;
        }

        var decoded = reader.ReadLiteral(bits);
        return decoded > max ? max : decoded;
    }

    private static int AlignPowerOfTwo(int value, int n)
    {
        var alignment = (1 << n) - 1;
        return (value + alignment) & ~alignment;
    }

    private readonly record struct Vp9ColorHeader(
        int BitDepth,
        Vp9ColorSpace ColorSpace,
        Vp9ColorRange ColorRange,
        int SubsamplingX,
        int SubsamplingY);
}
