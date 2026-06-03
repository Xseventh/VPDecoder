# VPDecoder

Managed .NET decoder experiments for raw VP8/VP9 frame packets.

The first target is raw VP9 frame packets extracted by an upstream container
parser. The core API intentionally does not know about WebM, IVF, WZ, MS, WCX,
or playback timelines.

Current status:

- Parses VP9 key-frame headers from raw packets.
- Parses VP9 tile buffer layout and validates tile size boundaries.
- Includes the VP9 bool/range reader used by compressed headers and tile data.
- Parses VP9 compressed-header transform mode, TX probability updates,
  coefficient probability updates, and skip probability updates.
- Builds key-frame decode state, dequant tables, YUV420 frame buffers, and
  libvpx-derived default coefficient probabilities, Pareto coefficient
  probability models, and TX32 scan tables.
- Probes early tile syntax for the provided samples: first superblock
  partition, first key-frame mode-info fields, first Y coefficient token, and
  full first Y TX32 coefficient block.
- Probes complete full-frame key-frame tile syntax for the provided main and
  alpha samples, including deterministic mode-info and residual coefficient
  group counts.
- Reconstructs deterministic unfiltered YUV420 frames for the provided main and
  alpha samples through intra prediction, inverse transform, and clipped
  transform edge handling.
- Applies the VP9 key-frame loop filter for the current profile0/8-bit/YUV420
  sample shape, including libvpx-style superblock masks and scalar 4/8/16-tap
  filters.
- Runs public raw VP9 decode through filtered YUV420 reconstruction and returns
  deterministic BGRA8888/RGBA8888/YUV420 output for the provided main and alpha
  samples.
- Maintains decoder-owned VP9 reference slots for decoded key frames, supports
  `show_existing_frame` replay with copied output, and clears those slots on
  `Reset()`. Reference updates use VP9 `refresh_frame_flags`, so future
  successful inter or intra-only frames can refresh only selected slots.
- Parses VP9 ordinary inter-frame and hidden intra-only uncompressed headers
  far enough to expose reference, refresh, size, motion, loop-filter,
  quantization, segmentation, and tile metadata. Restricted ordinary inter
  reconstruction is supported for the current gated shape: profile0/8-bit
  YUV420, single-reference blocks, same-size references, decoded spatial MV
  candidates, whole-pixel motion compensation, DCT residual groups, and scalar
  loop filtering. Unsupported inter features still return explicit diagnostics
  before pixels are emitted.
- Maintains decoder-owned VP9 frame-context slots for compressed-header
  probability state, commits refreshed contexts only after successful decode,
  and resets them with `Reset()`.
- Carries libvpx-derived VP9 inter probability defaults and drains ordinary
  inter compressed-header probability syntax for no-update streams, while
  keeping unsupported inter syntax strictly gated.
- Reads gated VP9 NEWMV syntax when a same-reference spatial MV candidate is
  already available; different-reference and previous-frame MV fallback,
  compound references, switchable interpolation, sub-8x8 inter blocks, and
  fractional-pixel motion compensation remain explicit unsupported paths.
- Preserves deterministic evidence for the first Block16X16 luma TX4 group
  that previously exposed residual synchronization drift.
- Converts decoded YUV420 frames to BGRA8888/RGBA8888 and composes alpha from
  either BGRA red or YUV luma.
- Exposes memory-first library entry points for WCX-style integration:
  `DecodeFrame(ReadOnlySpan<byte>)`, `DecodeFrame(ReadOnlyMemory<byte>)`,
  `DecodeFrameWithAlpha(...)`, and `Reset()`.
- Provides a small raw VP9 CLI smoke workflow in `src/VPDecoder.Cli`.
- Parses raw VP8 frame tags and key-frame uncompressed headers, then returns
  strict unsupported diagnostics until VP8 pixel reconstruction is implemented.
  The VP8 surface accepts `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` and
  uses the same displayed/no-display/failed result shape expected by sequence
  callers.
- Validates the current sample shape: VP9 profile 0, 8-bit, YUV420,
  2656x1352, 8 tile columns.
- Fails explicitly for unsupported decode work instead of emitting pixels.

Broader ordinary inter-frame prediction and VP8 pixel reconstruction remain
follow-up slices. The decoder must continue to return explicit unsupported
diagnostics until those pieces are complete.

Library API example:

```csharp
var decoder = new RawVp9Decoder();
var options = new Vp9DecodeOptions(
    ExpectedWidth: 2656,
    ExpectedHeight: 1352,
    OutputFormat: Vp9OutputPixelFormat.Bgra8888);

Vp9DecodeResult color = decoder.DecodeFrame(colorPacket, options);
Vp9DecodeResult merged = decoder.DecodeFrameWithAlpha(
    colorPacket,
    alphaPacket,
    options);

decoder.Reset();
```

`RawVp9Decoder` accepts `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` packet
inputs. It never requires file paths or container wrappers from callers; file
I/O is limited to the CLI smoke helper. Decode failures are returned as
structured `Vp9DecodeDiagnostic` values with stable codes and messages. Check
`Vp9DecodeResult.Status` to distinguish failed packets, displayed decoded
frames, and successful no-display frames.

Cross-frame decode semantics:

- A `RawVp9Decoder` instance represents one VP9 stream state. Feed packets for
  one video stream to the same instance in display/decode order.
- Use a fresh `RawVp9Decoder` or call `Reset()` before decoding a different
  stream, seeking to a point that does not have the required references, or
  replaying a stream from the beginning.
- Successful displayed key frames and supported inter frames update
  decoder-owned reference slots according to VP9 `refresh_frame_flags`.
  `show_existing_frame` can replay an existing reference without parsing a
  compressed header.
- Successful non-display frames also update decoder-owned state, then return
  `Vp9DecodeResultStatus.NoDisplayFrame` with no pixel buffer.
- Frame-context probability state is also decoder-owned. Refreshed contexts are
  committed only after a frame decodes successfully, and `Reset()` restores all
  frame contexts to defaults.
- If a packet fails with invalid, truncated, missing-reference, allocation, or
  unsupported-feature diagnostics before reconstruction completes, the decoder
  does not emit pixels for that packet. Callers should treat the stream as
  still positioned at the last successful decode unless a public API documents
  otherwise for a specific future mode.
- Returned frames are caller-owned pixel buffers. Mutating a returned frame does
  not mutate decoder reference slots.
- `DecodeFrameWithAlpha` maintains two decoder-owned stream states inside the
  same `RawVp9Decoder` instance: the primary color state and a lazy alpha state.
  Feed color and alpha packets for one stream to the same instance in order.
- `Reset()` clears both color and alpha state. Mixing `DecodeFrame` and
  `DecodeFrameWithAlpha` is allowed; `DecodeFrame` advances only color state,
  while `DecodeFrameWithAlpha` advances color first and alpha second.
- Color+alpha decode is non-transactional. If color succeeds but alpha fails,
  color state remains advanced and no merged pixels are returned. Callers should
  reset or stop the stream after such a failure unless they intentionally accept
  that state.

CLI smoke example:

```bash
dotnet run --project src/VPDecoder.Cli/VPDecoder.Cli.csproj -- \
  --input /tmp/vp9-main-frame-0.vp9 \
  --width 2656 \
  --height 1352
```

The command decodes the raw packet to BGRA8888 by default. Pass
`--format yuv420` or `--format rgba` to select another supported output shape,
and `--out frame.raw` to write the pixel buffer.

Alpha smoke example:

```bash
dotnet run --project src/VPDecoder.Cli/VPDecoder.Cli.csproj -- \
  --input /tmp/vp9-main-frame-0.vp9 \
  --alpha /tmp/vp9-alpha-frame-0.vp9 \
  --width 2656 \
  --height 1352 \
  --out frame.bgra
```
