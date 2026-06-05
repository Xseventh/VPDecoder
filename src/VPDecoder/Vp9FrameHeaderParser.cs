namespace VPDecoder;

public static class Vp9FrameHeaderParser
{
    internal static readonly int[] DefaultLoopFilterRefDeltas = [1, 0, -1, -1];
    internal static readonly int[] DefaultLoopFilterModeDeltas = [0, 0];

    private const int Vp9FrameMarker = 2;
    private const int SyncCode0 = 0x49;
    private const int SyncCode1 = 0x83;
    private const int SyncCode2 = 0x42;
    private const int ReferenceFrameCount = 8;
    private const int ReferencesPerFrame = 3;

    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        out Vp9FrameHeader? header,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TryParse(packet, referenceFrames: null, out header, out diagnostic);
    }

    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        IReadOnlyList<Vp9ReferenceFrameInfo?>? referenceFrames,
        out Vp9FrameHeader? header,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TryParse(
            packet,
            referenceFrames,
            DefaultLoopFilterRefDeltas,
            DefaultLoopFilterModeDeltas,
            out header,
            out diagnostic);
    }

    internal static bool TryParse(
        ReadOnlySpan<byte> packet,
        IReadOnlyList<Vp9ReferenceFrameInfo?>? referenceFrames,
        IReadOnlyList<int> loopFilterRefDeltas,
        IReadOnlyList<int> loopFilterModeDeltas,
        out Vp9FrameHeader? header,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        header = null;
        diagnostic = null;

        try
        {
            header = Parse(packet, referenceFrames, loopFilterRefDeltas, loopFilterModeDeltas);
            return true;
        }
        catch (Vp9HeaderParseException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static Vp9FrameHeader Parse(
        ReadOnlySpan<byte> packet,
        IReadOnlyList<Vp9ReferenceFrameInfo?>? referenceFrames = null,
        IReadOnlyList<int>? loopFilterRefDeltas = null,
        IReadOnlyList<int>? loopFilterModeDeltas = null)
    {
        loopFilterRefDeltas ??= DefaultLoopFilterRefDeltas;
        loopFilterModeDeltas ??= DefaultLoopFilterModeDeltas;
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
                0,
                true,
                0,
                new Vp9LoopFilterHeader(0, 0, false, false, [0, 0, 0, 0], [0, 0]),
                new Vp9QuantizationHeader(0, 0, 0, 0),
                new Vp9SegmentationHeader(false, false, false, false, false, [], []),
                new Vp9TileInfo(0, 0, 0, 0, 0, 0, 0),
                0,
                false,
                0,
                [],
                [],
                [],
                null,
                false,
                false,
                Vp9InterpolationFilter.None);
        }

        var frameType = reader.ReadBit() ? Vp9FrameType.InterFrame : Vp9FrameType.KeyFrame;
        var showFrame = reader.ReadBit();
        var errorResilientMode = reader.ReadBit();

        if (frameType != Vp9FrameType.KeyFrame)
        {
            return ReadInterFrameHeader(
                packet,
                referenceFrames,
                loopFilterRefDeltas,
                loopFilterModeDeltas,
                ref reader,
                frameMarker,
                profile,
                showFrame,
                errorResilientMode);
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
        var renderSizeDifferent = reader.ReadBit();
        if (renderSizeDifferent)
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
        var loopFilter = ReadLoopFilter(ref reader, DefaultLoopFilterRefDeltas, DefaultLoopFilterModeDeltas);
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
            0xff,
            frameParallelDecodingMode,
            frameContextIndex,
            loopFilter,
            quantization,
            segmentation,
            tileInfo,
            firstPartitionSize,
            false,
            0,
            [],
            [],
            [],
            null,
            renderSizeDifferent,
            false,
            Vp9InterpolationFilter.None);
    }

    private static Vp9FrameHeader ReadInterFrameHeader(
        ReadOnlySpan<byte> packet,
        IReadOnlyList<Vp9ReferenceFrameInfo?>? referenceFrames,
        IReadOnlyList<int> loopFilterRefDeltas,
        IReadOnlyList<int> loopFilterModeDeltas,
        ref Vp9BitReader reader,
        int frameMarker,
        int profile,
        bool showFrame,
        bool errorResilientMode)
    {
        var intraOnly = !showFrame && reader.ReadBit();
        var resetFrameContextMode = errorResilientMode ? 0 : reader.ReadLiteral(2);

        var bitDepth = 8;
        var colorSpace = Vp9ColorSpace.Bt601;
        var colorRange = Vp9ColorRange.Studio;
        var subsamplingX = 1;
        var subsamplingY = 1;
        int width;
        int height;
        int renderWidth;
        int renderHeight;
        int refreshFrameFlags;
        IReadOnlyList<int> referenceFrameIndices = [];
        IReadOnlyList<bool> referenceFrameSignBiases = [];
        IReadOnlyList<bool> frameSizeReferenceFlags = [];
        int? frameSizeReferenceIndex = null;
        var renderSizeDifferent = false;
        var allowHighPrecisionMv = false;
        var interpolationFilter = Vp9InterpolationFilter.None;

        if (intraOnly)
        {
            var syncCodeValid =
                reader.ReadLiteral(8) == SyncCode0 &&
                reader.ReadLiteral(8) == SyncCode1 &&
                reader.ReadLiteral(8) == SyncCode2;
            if (!syncCodeValid)
            {
                throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("Invalid VP9 intra-only frame sync code."));
            }

            if (profile > 0)
            {
                var color = ReadBitDepthColorSpaceAndSampling(ref reader, profile);
                bitDepth = color.BitDepth;
                colorSpace = color.ColorSpace;
                colorRange = color.ColorRange;
                subsamplingX = color.SubsamplingX;
                subsamplingY = color.SubsamplingY;
            }

            refreshFrameFlags = reader.ReadLiteral(ReferenceFrameCount);
            ReadFrameSize(ref reader, out width, out height);
            renderSizeDifferent = ReadRenderSize(ref reader, width, height, out renderWidth, out renderHeight);
        }
        else
        {
            width = 0;
            height = 0;
            refreshFrameFlags = reader.ReadLiteral(ReferenceFrameCount);
            var refIndices = new int[ReferencesPerFrame];
            var signBiases = new bool[ReferencesPerFrame];
            for (var i = 0; i < ReferencesPerFrame; i++)
            {
                refIndices[i] = reader.ReadLiteral(3);
                signBiases[i] = reader.ReadBit();
            }

            referenceFrameIndices = refIndices;
            referenceFrameSignBiases = signBiases;

            var sizeReferenceFlags = new bool[ReferencesPerFrame];
            var foundFrameSizeReference = false;
            for (var i = 0; i < ReferencesPerFrame; i++)
            {
                sizeReferenceFlags[i] = reader.ReadBit();
                if (sizeReferenceFlags[i])
                {
                    frameSizeReferenceIndex = i;
                    var referenceFrameSlot = refIndices[i];
                    var referenceInfo = GetReferenceFrameInfo(referenceFrames, referenceFrameSlot);
                    if (referenceInfo is null)
                    {
                        throw new Vp9HeaderParseException(
                            Vp9DecodeDiagnostic.MissingReferenceFrame(
                                $"VP9 inter-frame size references empty reference frame slot {referenceFrameSlot}."));
                    }

                    width = referenceInfo.Width;
                    height = referenceInfo.Height;
                    foundFrameSizeReference = true;
                    break;
                }
            }

            frameSizeReferenceFlags = sizeReferenceFlags;
            if (!foundFrameSizeReference)
            {
                ReadFrameSize(ref reader, out width, out height);
            }

            renderSizeDifferent = ReadRenderSize(ref reader, width, height, out renderWidth, out renderHeight);
            allowHighPrecisionMv = reader.ReadBit();
            interpolationFilter = ReadInterpolationFilter(ref reader);
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
        var previousRefDeltas = intraOnly || errorResilientMode
            ? DefaultLoopFilterRefDeltas
            : loopFilterRefDeltas;
        var previousModeDeltas = intraOnly || errorResilientMode
            ? DefaultLoopFilterModeDeltas
            : loopFilterModeDeltas;
        var loopFilter = ReadLoopFilter(ref reader, previousRefDeltas, previousModeDeltas);
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
            Vp9FrameType.InterFrame,
            showFrame,
            errorResilientMode,
            intraOnly,
            bitDepth,
            colorSpace,
            colorRange,
            subsamplingX,
            subsamplingY,
            width,
            height,
            renderWidth,
            renderHeight,
            refreshFrameContext,
            refreshFrameFlags,
            frameParallelDecodingMode,
            frameContextIndex,
            loopFilter,
            quantization,
            segmentation,
            tileInfo,
            firstPartitionSize,
            intraOnly,
            resetFrameContextMode,
            referenceFrameIndices,
            referenceFrameSignBiases,
            frameSizeReferenceFlags,
            frameSizeReferenceIndex,
            renderSizeDifferent,
            allowHighPrecisionMv,
            interpolationFilter);
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

    private static Vp9LoopFilterHeader ReadLoopFilter(
        ref Vp9BitReader reader,
        IReadOnlyList<int> previousRefDeltas,
        IReadOnlyList<int> previousModeDeltas)
    {
        var filterLevel = reader.ReadLiteral(6);
        var sharpnessLevel = reader.ReadLiteral(3);
        var refDeltas = CopyLoopFilterDeltas(previousRefDeltas, expectedLength: 4, nameof(previousRefDeltas));
        var modeDeltas = CopyLoopFilterDeltas(previousModeDeltas, expectedLength: 2, nameof(previousModeDeltas));

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

    private static int[] CopyLoopFilterDeltas(IReadOnlyList<int> source, int expectedLength, string parameterName)
    {
        if (source.Count != expectedLength)
        {
            throw new ArgumentException(
                $"VP9 loop-filter delta state must contain {expectedLength} entries.",
                parameterName);
        }

        var copy = new int[expectedLength];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = source[i];
        }

        return copy;
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
        maxLog2TileColumns--;

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

    private static void ReadFrameSize(ref Vp9BitReader reader, out int width, out int height)
    {
        width = reader.ReadLiteral(16) + 1;
        height = reader.ReadLiteral(16) + 1;
        if (width <= 0 || height <= 0)
        {
            throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.InvalidPacket("Invalid VP9 frame dimensions."));
        }
    }

    private static bool ReadRenderSize(
        ref Vp9BitReader reader,
        int width,
        int height,
        out int renderWidth,
        out int renderHeight)
    {
        renderWidth = width;
        renderHeight = height;
        var renderSizeDifferent = reader.ReadBit();
        if (renderSizeDifferent)
        {
            ReadFrameSize(ref reader, out renderWidth, out renderHeight);
        }

        return renderSizeDifferent;
    }

    private static Vp9InterpolationFilter ReadInterpolationFilter(ref Vp9BitReader reader)
    {
        if (reader.ReadBit())
        {
            return Vp9InterpolationFilter.Switchable;
        }

        return reader.ReadLiteral(2) switch
        {
            0 => Vp9InterpolationFilter.EightTapSmooth,
            1 => Vp9InterpolationFilter.EightTap,
            2 => Vp9InterpolationFilter.EightTapSharp,
            3 => Vp9InterpolationFilter.Bilinear,
            _ => throw new InvalidOperationException("Unexpected VP9 interpolation filter literal.")
        };
    }

    private static Vp9ReferenceFrameInfo? GetReferenceFrameInfo(
        IReadOnlyList<Vp9ReferenceFrameInfo?>? referenceFrames,
        int slot)
    {
        if (referenceFrames is null || slot < 0 || slot >= referenceFrames.Count)
        {
            return null;
        }

        var referenceInfo = referenceFrames[slot];
        if (referenceInfo is null)
        {
            return null;
        }

        if (referenceInfo.Width <= 0 || referenceInfo.Height <= 0)
        {
            throw new Vp9HeaderParseException(
                Vp9DecodeDiagnostic.InvalidPacket("VP9 reference frame metadata has invalid dimensions."));
        }

        return referenceInfo;
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
