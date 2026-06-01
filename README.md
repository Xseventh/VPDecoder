# VPDecoder

Managed .NET decoder experiments for raw VP8/VP9 frame packets.

The first target is raw VP9 frame packets extracted by an upstream container
parser. The core API intentionally does not know about WebM, IVF, WZ, MS, WCX,
or playback timelines.

Current status:

- Parses VP9 key-frame headers from raw packets.
- Parses VP9 tile buffer layout and validates tile size boundaries.
- Validates the current sample shape: VP9 profile 0, 8-bit, YUV420,
  2656x1352, 8 tile columns.
- Fails explicitly for unsupported decode work instead of emitting pixels.

Pixel reconstruction, alpha merge, inter frames, and VP8 are planned follow-up
slices.
