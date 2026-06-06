# VP9 Performance Optimization Plan

This document records the short-term optimization path for the managed VP9
decoder. It is based on a temporary instrumentation run over the
`/tmp/vpdecoder-repro` 97-frame sequence. The instrumentation was added only to
a `/private/tmp` copy of the repository and measured broad decode stages with
`Stopwatch`.

## Current Profile Snapshot

Reference workload:

- 97 color packets and 97 alpha packets from `/tmp/vpdecoder-repro`.
- 2656x1352 profile0 8-bit YUV420 VP9.
- Output measured primarily as `Yuv420`; `Bgra8888` was measured separately to
  isolate color conversion cost.
- Current correctness baseline is bitwise YUV420 alignment against libvpx for
  the repro sequence.

Approximate color `Yuv420` timing for 97 frames:

| Area | Approximate share |
| --- | ---: |
| Full inter-frame reconstruction path | 87% |
| Inter residual syntax probe | 52% |
| Inter coefficient group reads | 43% |
| Inter mode-info reads | 7% |
| Inter prediction | 26% |
| Single-reference motion compensation | 14% |
| Compound motion compensation | 2% |
| Loop filter | 10-11% |
| Coefficient block reads | 8-9% |
| Inter residual add | 3-4% |
| Intra block reconstruction inside inter frames | 3-4% |
| Header, compressed header, and tile layout parsing | < 1% |

Approximate allocation volume:

- Color `Yuv420`: about 9.9 GB allocated per 97 frames.
- Alpha `Yuv420`: about 9.2 GB allocated per 97 frames.
- Color `Bgra8888`: about 11.2 GB allocated per 97 frames.

The stage timings are nested, so rows do not add up to 100%. The allocation
numbers make the main near-term issue clear: the production inter decode path
still uses a probe/list/record-heavy two-pass structure that was useful while
bringing up correctness, but is too expensive for playback.

## Short-Term Optimization Order

1. Reduce obvious per-block allocations without changing decode semantics.
   - Avoid coefficient arrays for skipped blocks.
   - Reuse small scratch buffers where safe.
   - Preserve existing probe APIs and tests.

2. Split production inter decode from diagnostic probe decode.
   - Keep the current probe-returning path for tests and troubleshooting.
   - Add a production path that reads partition, mode-info, and residual syntax
     while reconstructing pixels.
   - Keep only the state needed for contexts, reference MV derivation, loop
     filter masks, and previous-frame MV metadata.

3. Consume coefficient syntax directly in the production path.
   - Decode residual coefficients into scratch buffers and immediately run
     inverse transform or DC-only paths.
   - Skip/eob=0 blocks should update contexts without allocating coefficient
     payload arrays.

4. Specialize scalar motion compensation.
   - Split whole-pixel, horizontal-only, vertical-only, and 2D subpel paths.
   - Add fixed-width loops for common VP9 block widths.
   - Reduce per-pixel filter lookup and bounds-check overhead.

5. Reduce loop filter overhead.
   - Cache thresholds for repeated levels.
   - Collapse small edge helper calls where they dominate.
   - Add fixed-width vertical and horizontal filter fast paths.

6. Optimize packed output conversion after YUV decode improves.
   - BGRA conversion is currently around 8-9% of packed output time, so it is
     not the first bottleneck.
   - SIMD YUV-to-BGRA remains useful once VP9 reconstruction cost comes down.

## Validation For Each Slice

Every optimization slice should keep the same correctness and diagnostics
surface:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame color and alpha sequence decode from `/tmp/vpdecoder-repro`.
- YUV420 bitwise comparison against the current libvpx reference dumps when
  those dumps are present locally.
- Record elapsed time and allocated MB for color and alpha `Yuv420`.

Unsupported VP9 and VP8 features must continue to return structured diagnostics
instead of silently emitting pixels.

## Optimization Log

### Zero-Coefficient Allocation Reduction

Initial slice:

- Added a cached SHA-256 value for all-zero coefficient arrays by transform
  size.
- Changed skipped/eob=0 coefficient blocks to bypass coefficient scanning and
  temporary SHA input byte-array allocation.
- Changed non-skipped coefficient parsing to read the first EOB bit before
  allocating coefficient and token-cache arrays.
- Changed non-zero coefficient hashing on little-endian platforms to hash the
  existing `int[]` memory as bytes instead of first allocating a second flattened
  byte array. Big-endian platforms keep the original explicit little-endian
  fallback.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence benchmark from `/tmp/vpdecoder-repro`

Observed short-run benchmark after the slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 7194-7333 ms | 6878 MB |
| Alpha `Yuv420` | 7245-7505 ms | 6447 MB |
| Color `Bgra8888` | 7982-8033 ms | 8206 MB |

Compared with the instrumentation snapshot, color `Yuv420` allocation dropped
from about 9.9 GB to about 6.9 GB per 97 frames. Alpha `Yuv420` allocation
dropped from about 9.2 GB to about 6.4 GB. This confirms coefficient probe
payload and diagnostic hash allocation are major contributors, while the next
large target remains the probe/list/record inter reconstruction structure.

Follow-up slice:

- Shared cached all-zero coefficient arrays by transform size for eob=0 blocks.
  These coefficient arrays are treated as read-only by decoder code.
- Moved the per-nonzero-block token cache from a heap `byte[]` allocation to a
  stack span.

Observed short-run benchmark after the follow-up:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 6173-6286 ms | 4680 MB |
| Alpha `Yuv420` | 6439-6534 ms | 4291 MB |
| Color `Bgra8888` | 7008-7070 ms | 6008 MB |

This brings color `Yuv420` allocation from about 9.9 GB down to about 4.7 GB
per 97 frames, and alpha `Yuv420` from about 9.2 GB down to about 4.3 GB. The
next large allocation source is still the retained inter probe/list/record
metadata, especially coefficient block probes for nonzero residual blocks.

Compact reconstructed-frame metadata slice:

- Added compact inter reconstructed-frame metadata that stores the predicted
  inter mode blocks needed by loop filtering and previous-frame MV state without
  also retaining coefficient group payloads inside `Vp9ReconstructedFrame`.
- Kept the diagnostic `predictedProbes` output intact for tests and debugging.

Observed short-run benchmark after the compact metadata slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 6031-6143 ms | 4636 MB |
| Alpha `Yuv420` | 6358-6410 ms | 4260 MB |
| Color `Bgra8888` | 6910-6943 ms | 5965 MB |

The compact metadata change is a smaller win than the zero-coefficient work,
but it removes unnecessary reconstructed-frame retention and prepares the
production decode path for a fuller split from diagnostic probe storage.
