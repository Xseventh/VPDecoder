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
  `Reset()`.
- Parses VP9 ordinary inter-frame and hidden intra-only uncompressed headers
  far enough to expose reference, refresh, size, motion, loop-filter,
  quantization, segmentation, and tile metadata before returning strict
  unsupported diagnostics for inter prediction. Inter frame sizes that are
  coded from reference frames use decoder-owned reference metadata when
  available and otherwise return `MissingReferenceFrame`.
- Preserves deterministic evidence for the first Block16X16 luma TX4 group
  that previously exposed residual synchronization drift.
- Converts decoded YUV420 frames to BGRA8888/RGBA8888 and composes alpha from
  either BGRA red or YUV luma.
- Provides a small raw VP9 CLI smoke workflow in `src/VPDecoder.Cli`.
- Parses raw VP8 frame tags and key-frame uncompressed headers, then returns
  strict unsupported diagnostics until VP8 pixel reconstruction is implemented.
- Validates the current sample shape: VP9 profile 0, 8-bit, YUV420,
  2656x1352, 8 tile columns.
- Fails explicitly for unsupported decode work instead of emitting pixels.

Ordinary inter-frame prediction and VP8 pixel reconstruction remain follow-up
slices. The decoder must continue to return explicit unsupported diagnostics
until those pieces are complete.

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
