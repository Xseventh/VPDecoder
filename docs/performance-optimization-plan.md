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
- `dotnet run --project tools/VPDecoder.Bench -c Release -- <packet-root> color yuv420 <iterations> <frames>`
- `dotnet run --project tools/VPDecoder.Bench -c Release -- <packet-root> alpha yuv420 <iterations> <frames>`
- `dotnet run --project tools/VPDecoder.Bench -c Release -- <packet-root> merged bgra8888 <iterations> <frames>`
- External 97-frame color and alpha repro sequence decode.
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

Loop-filter empty-bit skip slice:

- Added precomputed vertical and horizontal active masks in luma and chroma
  loop-filter application.
- Skipped threshold lookup and edge-helper dispatch when the current mask bit
  has no 16x16, 8x8, 4x4, or internal 4x4 edge to filter.
- Kept the top-row horizontal special cases intact so frame-boundary behavior
  remains unchanged.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).

Observed short-run benchmark after empty loop-filter mask-bit skipping:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 2935-2987 ms | 3725 MB |
| Alpha `Yuv420` | 3242-3286 ms | 3592 MB |
| Color `Bgra8888` | 3854-3867 ms | 5054 MB |

This is a small CPU win over the lazy threshold-cache slice. It trims empty
loop-filter dispatch work without changing allocation meaningfully.

Pair-based packed color conversion slice:

- Split YUV420-to-packed conversion into concrete Studio/BGRA, Studio/RGBA,
  Full/BGRA, and Full/RGBA paths.
- Moved output-format and range branches out of the per-pixel hot loop.
- Reused each YUV420 chroma sample for the two luma pixels that share it,
  computing chroma contributions once per pair instead of once per pixel.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).
- Packed color benchmark checksum remained unchanged at
  `3711330852910723308`.

Observed short-run benchmark after pair-based packed conversion:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 2979-2991 ms | 3725 MB |
| Alpha `Yuv420` | 3191-3251 ms | 3592 MB |
| Color `Bgra8888` | 3667-3688 ms | 5054 MB |

This targets the packed-output-only overhead. Color `Bgra8888` drops from about
3.85 seconds to about 3.67 seconds over the 97-frame repro sequence, while YUV
decode timings remain in the same range as the loop-filter slice.

Reusable production residual scratch slice:

- Added a scratch-aware production overload for inter residual read/add.
- Changed the direct inter reconstruction path to rent coefficient and token
  scratch buffers once for the direct decode operation instead of renting and
  returning them for every inter plane.
- Kept the existing residual API as a compatibility wrapper for tests and
  diagnostic/probe callers.
- Kept intra-in-inter residual group scratch behavior unchanged.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).
- Packed color benchmark checksum remained unchanged at
  `3711330852910723308`.

Observed short-run benchmark after reusable residual scratch:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 2938-2963 ms | 3725 MB |
| Alpha `Yuv420` | 3179-3255 ms | 3592 MB |
| Color `Bgra8888` | 3648-3746 ms | 5054 MB |

This is a small CPU cleanup rather than an allocation win. It removes repeated
ArrayPool traffic from the hottest production residual path and keeps packed
output checksum and YUV bitwise alignment unchanged. The alpha and packed-output
timings remain noisy, but the color `Yuv420` runs are consistently at or below
the previous pair-based conversion baseline.

Non-committed local trials that did not show stable benefit:

- Studio-range luma lookup table in packed color conversion.
- Little-endian BGRA `uint` word writes.
- Removing the hybrid inverse-transform row scratch clear.

These were reverted because benchmark results were neutral or slightly worse.

Residual loop invariant cleanup slice:

- Hoisted inter transform type and frame context lookup out of the per-transform
  residual block loop.
- Fetched reusable production coefficient/token scratch spans once per inter
  mode block instead of once per plane.
- Kept the same residual parsing and reconstruction APIs; this is only a hot
  loop shape cleanup.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).
- Packed color benchmark checksum remained unchanged at
  `3711330852910723308`.

Observed short-run benchmark after residual loop invariant cleanup:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 2932-2946 ms | 3725 MB |
| Alpha `Yuv420` | 3220-3231 ms | 3592 MB |
| Color `Bgra8888` | 3695-3731 ms | 5054 MB |

This is another small CPU-only cleanup. The main value is keeping residual
production loops in a JIT-friendly shape before moving to larger metadata and
motion-compensation changes.

In-place alpha merge slice:

- Added an internal BGRA+BGRA alpha merge path that writes the alpha channel
  into the temporary color output frame produced by `DecodeFrameWithAlpha`.
- Kept the public `Vp9AlphaComposer.MergeBgraWithBgraAlpha` method clone-based,
  so standalone composer callers keep non-mutating input semantics.
- Added tests for the public non-mutating merge and the internal in-place merge.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- 97-frame repro sequence YUV420 comparison against libvpx for color and alpha
  frames 0, 51, 72, and 96; all Y/U/V totals remained bitwise identical
  (`mae=0`, `rmse=0`, `maxAbs=0`).
- Merged color+alpha BGRA benchmark checksum remained unchanged at
  `10561845111562233601`.

Observed short-run merged color+alpha benchmark:

| Merge path | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Clone color frame | 8019-8102 ms | 11303 MB |
| In-place temporary color frame | 7938-8004 ms | 9974 MB |

This removes about 1.3 GB of allocation over the 97-frame color+alpha repro
sequence by avoiding one full BGRA clone per visible merged frame. It is mainly
a WCX-facing memory improvement; elapsed time is slightly better but still
dominated by decoding both color and alpha streams.

Alpha YUV red-channel merge slice:

- Changed `DecodeFrameWithAlpha` to decode the alpha packet as `Yuv420`
  instead of a full `Bgra8888` frame.
- Added a direct YUV420-to-alpha merge helper that computes the same red channel
  value that packed BGRA conversion would have produced, then writes it into the
  color frame's alpha channel.
- Kept the public clone-based `MergeBgraWithYuvAlpha` behavior unchanged and
  kept `DecodeFrameWithAlpha` output hashes unchanged.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- Merged color+alpha BGRA benchmark checksum remained unchanged at
  `10561845111562233601`.

Observed short-run merged color+alpha benchmark after the slice:

| Merge path | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Alpha decoded as BGRA | 7938-8004 ms | 9974 MB |
| Alpha decoded as YUV420 with direct red-channel merge | 7335-7408 ms | 8650 MB |

This removes another roughly 1.3 GB of allocation over the 97-frame color+alpha
repro sequence and trims about 7-8% from merged BGRA elapsed time in the short
run. The win comes from avoiding the alpha stream's temporary packed BGRA frame
and computing only the red channel needed for alpha composition.

Stack-allocated syntax probability buffers slice:

- Changed hot VP9 mode-info probability tables from per-read small `byte[]`
  allocations to stack spans.
- Changed motion-vector fractional probability reading to reuse frame-context
  arrays or a stack span instead of cloning/allocating a three-entry array per
  component.
- Marked the VP9 tree-reader span inputs as `scoped` so stack-backed
  probability spans cannot escape.

Validation:

- `dotnet build VPDecoder.slnx --no-restore -m:1`
- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- Color, alpha, and merged benchmark checksums remained unchanged.

Observed short-run benchmark after the slice:

| Stream/output | Elapsed range | Allocated MB |
| --- | ---: | ---: |
| Color `Yuv420` | 2863-2980 ms | 3721 MB |
| Alpha `Yuv420` | 3145-3255 ms | 3616 MB |
| Merged `Bgra8888` | 7159-7548 ms | 8644 MB |

This is a small allocation cleanup rather than a clear CPU win. The main value
is removing frequent tiny syntax-path allocations before moving on to larger
motion-compensation and loop-filter work.

Scalar loop-filter traversal slice:

- Added a per-frame 64-entry loop-filter threshold table, backed by stack
  storage, so hot edge application no longer builds nullable per-superblock
  threshold caches.
- Changed luma and chroma loop-filter application to iterate active mask bits
  in row-major order instead of scanning every grid position and checking for
  empty bits.
- Preserved the existing edge order, border handling, and scalar filter
  formulas.
- Replaced hot `Math.Abs(byte - byte)` checks with an inlined byte absolute
  difference helper.

Validation:

- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- Full 97-frame color and alpha YUV420 comparison against libvpx; both streams
  remained bitwise identical for every frame.
- Color, alpha, and merged benchmark checksums remained unchanged.

Observed short-run benchmark after the slice:

| Stream/output | Average elapsed | Elapsed range | Allocated MB |
| --- | ---: | ---: | ---: |
| Color `Yuv420` | 2624 ms | 2594-2660 ms | 3611 MB |
| Alpha `Yuv420` | 2558 ms | 2514-2618 ms | 3490 MB |
| Merged `Bgra8888` | 6042 ms | 5984-6114 ms | 7429 MB |

Compared with the previous scalar reconstruction checkpoint, the same
benchmark moved from about 2687 ms to 2624 ms for color `Yuv420`, from about
2615 ms to 2558 ms for alpha `Yuv420`, and from about 6185 ms to 6042 ms for
merged `Bgra8888`. This is a modest CPU win, but it keeps the loop-filter path
closer to libvpx's active-mask traversal shape without introducing SIMD or
unsafe code.

Motion-compensation clip-shape slice:

- Changed the motion-compensation `ClipPixel` helper from a two-branch
  min/max-style ternary to an unsigned in-range check with a single saturation
  fallback.
- Kept all interpolation, prediction, and edge-clamping semantics unchanged.
- This targets the hot subpel prediction path where every filtered pixel is
  rounded and clipped.

Validation:

- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- Full 97-frame color and alpha YUV420 comparison against libvpx; both streams
  remained bitwise identical for every frame.
- Color, alpha, and merged benchmark checksums remained unchanged.

Observed short-run benchmark after the slice:

| Stream/output | Average elapsed | Elapsed range | Allocated MB |
| --- | ---: | ---: | ---: |
| Color `Yuv420` | 2598 ms | 2495-2641 ms | 3611 MB |
| Alpha `Yuv420` | 2468 ms | 2422-2544 ms | 3490 MB |
| Merged `Bgra8888` | 6082 ms | 5552-7142 ms | 7429 MB |

The color and alpha `Yuv420` paths both improved in this short run, while the
merged packed run had a large outlier and should be treated as noisy. The
change is intentionally tiny and keeps the scalar motion-compensation code in a
JIT-friendly shape without changing the broader architecture.

Previous-frame MV grid storage slice:

- Replaced the nullable previous-frame MV grid array with separate value and
  occupancy arrays.
- Kept previous-frame MV lookup semantics unchanged: empty MI slots still
  return no candidate, while populated slots return the same reference and
  compound motion-vector data.
- This targets both allocation size and lookup shape in the inter prediction
  candidate path.

Validation:

- `dotnet test VPDecoder.slnx -m:1 --no-restore`
- Full 97-frame color and alpha YUV420 comparison against libvpx; both streams
  remained bitwise identical for every frame.
- Color, alpha, and merged benchmark checksums remained unchanged.

Observed short-run benchmark after the slice:

| Stream/output | Average elapsed | Elapsed range | Allocated MB |
| --- | ---: | ---: | ---: |
| Color `Yuv420` | 2190 ms | 2185-2194 ms | 3596 MB |
| Alpha `Yuv420` | 2048 ms | 2035-2057 ms | 3475 MB |
| Merged `Bgra8888` | 5089 ms | 5059-5117 ms | 7399 MB |

Compared with the repository benchmark baseline immediately before this slice,
allocation dropped by roughly 15 MB per single stream and roughly 31 MB for
merged color+alpha. The alpha and merged elapsed times also improved in this
short run, while color `Yuv420` was essentially flat.

Additional non-committed local trials that did not show stable benefit:

- Grid-only direct inter mode metadata: reduced allocation by about 10 MB over
  the 97-frame repro but made color `Yuv420` consistently slower.
- Generic compound unclamped motion-compensation loop: preserved bitwise output
  but made color, alpha, and packed runs slower.
- Reference refresh flag zero-clone guard: correct in isolation, but the repro
  did not show a measurable current-path benefit.
