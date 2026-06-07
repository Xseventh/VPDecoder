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

## Current Near-Term Path

The residual coefficient path is no longer the only large allocation source.
After the direct reconstruction and coefficient scratch-pool slices, the next
short-term work should prioritize metadata and ownership overhead before
larger SIMD work:

- Keep production ordinary inter decode separate from diagnostic probe decode.
- Let production loop filtering consume compact inter mode metadata directly,
  without rebuilding key-frame-style reconstructed mode wrappers.
- Keep predicted inter mode blocks until previous-frame MV prediction has a
  smaller purpose-built state representation.
- Avoid changing reference-frame clone semantics until the public decoded-frame
  ownership contract is made explicit. Returning mutable `byte[]` output means
  the reference store still needs to protect itself from caller mutation.
- Re-profile after metadata wins before starting motion-compensation SIMD or
  packed-output SIMD work.

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

Direct inter reconstruction production path:

- Added a production-only ordinary inter reconstruction path that consumes tile
  partition, mode-info, and residual syntax while writing pixels immediately.
- Kept the existing probe-returning path for diagnostics and tests.
- Shared the mode-info parser between both paths so unsupported features and
  motion-vector syntax stay consistent.
- Removed the `packet.ToArray()` copy from the ordinary inter production path by
  accepting the raw packet as `ReadOnlySpan<byte>`.
- The direct path still allocates coefficient group probes per decoded block;
  it no longer retains full-frame superblock probe lists before reconstruction.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the direct production path:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5326-5453 ms | 4470 MB |
| Alpha `Yuv420` | 5987-6075 ms | 4143 MB |
| Color `Bgra8888` | 6320-6371 ms | 5799 MB |

This slice mainly removes the second pass over retained full-frame inter
probes, so the elapsed-time win is larger than the allocation win. The next
highest-impact step is to stop materializing coefficient group probes in the
production path and feed inverse transform/residual add directly from scratch
coefficient buffers.

Production residual scratch-block slice:

- Added a production inter residual reader that fills reusable per-plane
  coefficient block lists instead of creating a coefficient group record and a
  fresh block list for each plane.
- Added a block-list based inter residual adder so the direct reconstruction
  path can consume those scratch lists directly.
- Kept the existing coefficient group APIs for probe/debug paths.
- Kept release builds from allocating per-group duplicate-offset tracking in
  inter residual add; debug builds still validate uniqueness.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the scratch-block slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5263-5367 ms | 4129 MB |
| Alpha `Yuv420` | 5935-6023 ms | 3954 MB |
| Color `Bgra8888` | 6278-6303 ms | 5458 MB |

This reduces another roughly 340 MB from color `Yuv420` and packed `Bgra8888`
over the 97-frame repro sequence, mostly by reusing the per-plane block-list
containers. The next step is more invasive: decode coefficient payloads into
reusable scratch arrays and remove the per-transform `Vp9CoefficientBlockProbe`
record allocation from the production path.

Production coefficient block data slice:

- Split coefficient parsing into a lightweight `Vp9CoefficientBlockData` core
  plus the existing diagnostic `Vp9CoefficientBlockProbe` wrapper.
- Changed the ordinary inter production path to predict the block first, then
  read each inter residual transform and immediately add it to the predicted
  pixels.
- Removed the production path's per-plane scratch block lists and per-transform
  coefficient probe records.
- Kept the older probe and coefficient-group paths for diagnostic APIs.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the coefficient block data slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5268-5314 ms | 4015 MB |
| Alpha `Yuv420` | 6014-6112 ms | 3872 MB |
| Color `Bgra8888` | 6184-6200 ms | 5344 MB |

This removes another roughly 110 MB from color `Yuv420` and `Bgra8888` over
the 97-frame repro sequence while keeping color elapsed time stable and packed
output slightly faster. Alpha elapsed time moved a little slower in this short
run, likely because the one-block add path recomputes plane geometry for each
nonzero transform. The next targeted optimization should cache per-plane inter
residual geometry while keeping the immediate-add structure.

Production residual plane-context slice:

- Added a stack-only inter residual plane context that caches plane pixels,
  stride, block origin, visible geometry, transform size, and transform offsets
  once per plane.
- Changed the production coefficient reader to reuse that context for every
  nonzero transform in the plane.
- Kept the immediate-add residual structure and older diagnostic group APIs.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the residual plane-context slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5192-5226 ms | 4015 MB |
| Alpha `Yuv420` | 5769-5861 ms | 3876 MB |
| Color `Bgra8888` | 6032-6060 ms | 5344 MB |

This mostly improves elapsed time rather than allocation. It recovers the alpha
regression from the previous immediate-add slice and gives packed output a
noticeable short-run improvement. The remaining allocation is now dominated by
coefficient payload arrays, reconstructed/predicted mode metadata, frame
buffers, and packed output buffers.

Production DC-only coefficient payload slice:

- Changed the lightweight production coefficient block data to represent eob=1
  DCT/DC-only inter residuals with just the DC value.
- The diagnostic probe wrapper still materializes dense coefficient arrays when
  SHA/hash metadata is requested.
- The inter residual adder now writes non-clipped DC-only blocks directly
  without allocating a full coefficient array.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the DC-only payload slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5092-5211 ms | 3997 MB |
| Alpha `Yuv420` | 5815-6212 ms | 3842 MB |
| Color `Bgra8888` | 6138-6289 ms | 5326 MB |

This is a small allocation win: about 18 MB less for color `Yuv420` and
`Bgra8888`, and about 34 MB less for alpha `Yuv420` over the repro sequence.
Elapsed time is roughly neutral/noisy in this short run. The next allocation
target is the non-DC coefficient arrays, which needs reusable scratch buffers or
direct transform input without breaking diagnostic probe materialization.

Production coefficient scratch-pool slice:

- Added a production-only coefficient reader that decodes non-DC inter residual
  transforms into per-plane `ArrayPool` scratch buffers and immediately applies
  the inverse transform.
- Kept the diagnostic coefficient-block data/probe path materializing dense
  arrays for probe APIs and hashes.
- Continued using the DC-only metadata path for eob=1 DCT/DC-only blocks.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the coefficient scratch-pool slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5223-5340 ms | 3944 MB |
| Alpha `Yuv420` | 5812-5873 ms | 3760 MB |
| Color `Bgra8888` | 6039-6246 ms | 5274 MB |

This is a larger allocation win than the DC-only-only slice: about 53 MB less
for color `Yuv420`, about 82 MB less for alpha `Yuv420`, and about 52 MB less
for packed `Bgra8888` over the repro sequence. Elapsed time is mostly neutral
with short-run noise from pool rent/return and machine load. Further allocation
work should now focus on reconstructed/predicted mode metadata and output
buffer lifecycle rather than residual coefficient payloads alone.

Production inter loop-filter metadata slice:

- Changed ordinary inter production decode to return the reconstructed YUV frame
  plus predicted inter mode blocks directly instead of wrapping them in
  `Vp9ReconstructedFrame`.
- Added a loop-filter entry that consumes compact inter mode metadata directly.
- Built inter loop-filter superblock masks in a single indexed pass over mode
  blocks, avoiding the previous reconstructed-mode wrapper list, synthetic
  `Vp9ModeInfoProbe` objects, mode grid, and superblock dictionary grouping.
- Kept the diagnostic/probe reconstruction paths intact.
- Kept reference-frame cloning unchanged because decoded output pixels remain
  mutable to callers.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the inter loop-filter metadata slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5085-5157 ms | 3821 MB |
| Alpha `Yuv420` | 5919-6018 ms | 3656 MB |
| Color `Bgra8888` | 5927-5945 ms | 5150 MB |

This removes roughly 123 MB from color `Yuv420`, 104 MB from alpha `Yuv420`,
and 124 MB from packed `Bgra8888` over the 97-frame repro sequence compared
with the coefficient scratch-pool slice. The elapsed-time result is also mildly
better for color and packed output in this run, but should still be treated as
short-run data because local machine load varies.

Fixed MV candidate-set slice:

- Replaced the production motion-vector candidate `List<Vp9MotionVector>` with
  a fixed two-entry value type, matching VP9's candidate limit.
- Added fixed-set clamp and selection overloads so ordinary inter prediction and
  motion-vector parsing avoid per-block list and clamp-array allocations.
- Kept the public candidate-list helpers intact for tests and diagnostic callers
  by materializing a list only at that boundary.
- Preserved duplicate candidate behavior after border clamping, because
  `NEARMV` selection depends on candidate order and count.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the fixed MV candidate-set slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 5178-5281 ms | 3742 MB |
| Alpha `Yuv420` | 5661-5714 ms | 3609 MB |
| Color `Bgra8888` | 5993-6228 ms | 5071 MB |

This removes roughly 79 MB from color `Yuv420`, 47 MB from alpha `Yuv420`,
and 79 MB from packed `Bgra8888` over the 97-frame repro sequence compared
with the inter loop-filter metadata slice. Elapsed time is mixed in this short
run; the more reliable signal is reduced allocation from eliminating frequent
small candidate containers.

Tile-local spatial MV lookup grid slice:

- Added a production-only tile-local MI grid for decoded and predicted inter
  mode blocks.
- Changed ordinary inter motion-vector candidate lookup to use O(1) grid
  lookup instead of reverse-scanning the decoded/predicted mode-block lists for
  each neighbor probe.
- Kept diagnostic/probe paths on the existing list-scan implementation.
- Preserved tile-local lookup semantics: the grid is created per tile and does
  not return candidates outside the current tile.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the tile-local spatial MV lookup grid slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 3754-3760 ms | 3833 MB |
| Alpha `Yuv420` | 4915-5085 ms | 3701 MB |
| Color `Bgra8888` | 4606-4618 ms | 5162 MB |

This is the first clearly CPU-dominant win in the optimization series: color
`Yuv420` drops from roughly 5.2 seconds to 3.75 seconds over the 97-frame repro
sequence, and packed `Bgra8888` drops to about 4.61 seconds. Allocation rises
by about 91-92 MB compared with the fixed candidate-set slice because each tile
now allocates lookup grids. The next low-risk follow-up is to pool or otherwise
reuse those grid backing arrays while preserving deterministic clearing.

Pooled spatial MV lookup grid slice:

- Changed the production tile-local MV lookup grids to rent backing arrays from
  `ArrayPool<T>`.
- Added deterministic clearing before returning each grid to the pool.
- Scoped the pooled grids with `using` in the direct inter tile loop so early
  diagnostics and exceptions still return arrays.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the pooled spatial MV lookup grid slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 3797-3864 ms | 3742 MB |
| Alpha `Yuv420` | 5195-5363 ms | 3609 MB |
| Color `Bgra8888` | 4646-4655 ms | 5071 MB |

This recovers the roughly 90 MB allocation regression introduced by the
tile-local lookup grids while keeping most of the CPU win. Color `Yuv420` and
packed `Bgra8888` allocation are back near the fixed candidate-set slice, while
elapsed time remains around 3.8 seconds for color `Yuv420` and 4.65 seconds for
packed `Bgra8888` on this repro run.

Single-reference subpel motion-compensation fast-path slice:

- Split the single-reference fractional prediction path into horizontal-only,
  vertical-only, and two-dimensional subpel loops.
- Added unclamped inner loops for blocks whose full filter tap footprint is
  inside the reference plane.
- Kept the existing clamped filter helpers for border blocks and left compound
  prediction unchanged for this slice.
- Cached interpolation kernels once per block instead of slicing the kernel
  table for every predicted pixel.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after the single-reference subpel fast path:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 3114-3125 ms | 3742 MB |
| Alpha `Yuv420` | 3590-3642 ms | 3609 MB |
| Color `Bgra8888` | 3949-3972 ms | 5071 MB |

This is a CPU-only win over the pooled spatial-grid slice: allocations stay
flat, while color `Yuv420` drops by roughly another 18%, alpha `Yuv420` drops
by about 30%, and packed `Bgra8888` drops by about 15% on the repro workload.
The result confirms that scalar motion-compensation shape was the next high
leverage target before SIMD work.

Compound subpel average kernel-reuse slice:

- Changed compound prediction to cache each reference block's x/y interpolation
  kernels once per block.
- Reused the single-reference unclamped/clamped pixel helpers when averaging
  fractional compound predictors.
- Kept the existing whole-pixel compound row-average fast path unchanged.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after compound subpel kernel reuse:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 3034-3052 ms | 3742 MB |
| Alpha `Yuv420` | 3262-3308 ms | 3609 MB |
| Color `Bgra8888` | 3855-3881 ms | 5071 MB |

This is another CPU-only improvement. It is smaller than the single-reference
fast path for color frames, but alpha benefits more on this sequence. The
managed scalar path is now near 3.0 seconds for color `Yuv420` and below
3.9 seconds for packed color output over the 97-frame repro sequence.

Lazy loop-filter threshold cache slice:

- Changed each loop-filter superblock mask to allocate the per-level threshold
  cache lazily.
- Kept the base frame-level threshold stored directly on the mask, so the
  common same-level path does not allocate an extra 64-entry nullable cache.
- Kept mode/ref delta threshold caching behavior unchanged when a block uses a
  filter level different from the mask's base level.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after lazy loop-filter threshold caches:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 3002-3061 ms | 3726 MB |
| Alpha `Yuv420` | 3328-3350 ms | 3592 MB |
| Color `Bgra8888` | 3906-3907 ms | 5054 MB |

This is primarily an allocation cleanup. CPU time stays close to the previous
compound subpel slice, while allocation drops by roughly 16 MB for color
`Yuv420`, 17 MB for alpha `Yuv420`, and 17 MB for packed color output over the
97-frame repro sequence.
