namespace VPDecoder;

internal sealed class Vp9ReferenceFrameStore
{
    private const int ReferenceFrameCount = 8;

    private readonly Vp9ReferenceFrame?[] _frames = new Vp9ReferenceFrame?[ReferenceFrameCount];

    public int Count => _frames.Length;

    public void Reset()
    {
        Array.Clear(_frames);
    }

    public void Refresh(Vp9DecodedFrame frame, Vp9ColorRange colorRange, int refreshFrameFlags)
    {
        var referenceFrame = new Vp9ReferenceFrame(CloneFrame(frame), colorRange);
        for (var i = 0; i < _frames.Length; i++)
        {
            if (((refreshFrameFlags >> i) & 1) != 0)
            {
                _frames[i] = referenceFrame;
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

    public Vp9ReferenceFrameInfo?[] CreateFrameInfos()
    {
        var referenceFrameInfos = new Vp9ReferenceFrameInfo?[_frames.Length];
        for (var i = 0; i < _frames.Length; i++)
        {
            var referenceFrame = _frames[i]?.Frame;
            if (referenceFrame is not null)
            {
                referenceFrameInfos[i] = new Vp9ReferenceFrameInfo(referenceFrame.Width, referenceFrame.Height);
            }
        }

        return referenceFrameInfos;
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

internal sealed record Vp9ReferenceFrame(Vp9DecodedFrame Frame, Vp9ColorRange ColorRange);
