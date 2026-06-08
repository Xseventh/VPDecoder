namespace VPDecoder;

internal sealed class Vp9ReferenceFrameStore
{
    private const int ReferenceFrameCount = 8;

    private readonly Vp9ReferenceFrame?[] _frames = new Vp9ReferenceFrame?[ReferenceFrameCount];
    private readonly Vp9ReferenceFrameInfo?[] _frameInfos = new Vp9ReferenceFrameInfo?[ReferenceFrameCount];

    public int Count => _frames.Length;

    public void Reset()
    {
        Array.Clear(_frames);
        Array.Clear(_frameInfos);
    }

    public void Refresh(Vp9DecodedFrame frame, Vp9ColorRange colorRange, int refreshFrameFlags)
    {
        Refresh(frame, Vp9ColorSpace.Bt601, colorRange, refreshFrameFlags);
    }

    public void Refresh(
        Vp9DecodedFrame frame,
        Vp9ColorSpace colorSpace,
        Vp9ColorRange colorRange,
        int refreshFrameFlags,
        bool cloneFrame = true)
    {
        if ((refreshFrameFlags & 0xff) == 0)
        {
            return;
        }

        var referenceFrame = new Vp9ReferenceFrame(cloneFrame ? CloneFrame(frame) : frame, colorSpace, colorRange);
        for (var i = 0; i < _frames.Length; i++)
        {
            if (((refreshFrameFlags >> i) & 1) != 0)
            {
                _frames[i] = referenceFrame;
                _frameInfos[i] = new Vp9ReferenceFrameInfo(referenceFrame.Frame.Width, referenceFrame.Frame.Height);
            }
        }
    }

    public bool TryGet(int slot, out Vp9ReferenceFrame? referenceFrame)
    {
        if (slot < 0 || slot >= _frames.Length)
        {
            referenceFrame = null;
            return false;
        }

        referenceFrame = _frames[slot];
        return referenceFrame is not null;
    }

    public IReadOnlyList<Vp9ReferenceFrameInfo?> CreateFrameInfos()
    {
        return _frameInfos;
    }

    public Vp9DecodeDiagnostic? ValidateInterFrameReferences(Vp9FrameHeader header)
    {
        if (header.ShowExistingFrame || header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            return null;
        }

        if (header.Profile != 0 || header.BitDepth != 8 || header.SubsamplingX != 1 || header.SubsamplingY != 1)
        {
            return null;
        }

        foreach (var slot in header.ReferenceFrameIndices)
        {
            if (!TryGet(slot, out _))
            {
                return Vp9DecodeDiagnostic.MissingReferenceFrame(
                    $"VP9 inter frame references empty reference frame slot {slot}.");
            }
        }

        return null;
    }

    public static Vp9DecodedFrame CloneFrame(Vp9DecodedFrame frame)
    {
        var pixels = new byte[frame.Pixels.Length];
        frame.Pixels.AsSpan().CopyTo(pixels);

        return frame.PixelFormat == Vp9OutputPixelFormat.Yuv420
            ? Vp9DecodedFrame.CreateYuv420(
                frame.Width,
                frame.Height,
                pixels,
                frame.Planes[0],
                frame.Planes[1],
                frame.Planes[2])
            : Vp9DecodedFrame.CreatePacked(
                frame.Width,
                frame.Height,
                frame.PixelFormat,
                pixels,
                frame.Stride);
    }
}

internal sealed record Vp9ReferenceFrame(Vp9DecodedFrame Frame, Vp9ColorSpace ColorSpace, Vp9ColorRange ColorRange);
