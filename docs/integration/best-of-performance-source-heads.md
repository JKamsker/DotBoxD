# Best-of-performance source heads

This ledger is updated on `integration/best-of-performance` whenever the two
source branches are reviewed for the best-of-performance integration loop.

Use the hashes in **Current reviewed source heads** as the previous cut points
for the next sync. Compare each source branch from the recorded hash, exclusive,
to its current head.

## Current reviewed source heads

- `claude/charming-napier-4d62f4`
  - Reviewed through: `aea170e1fb68a3466c5727b85fae78db66f97d52`
  - Previous reviewed head: `a0be587eb463f5e7c6b7dc97a0c67b2271b116fd`
  - Decision: imported into `integration/best-of-performance`
  - Integration tip after import: `237e2f2377b80ffa805f67f858c3b6bc7fd7812d`
- `perf/improve-performance1`
  - Reviewed through: `ed1923d87382a84b2df98e03310cfa924a585794`
  - Previous reviewed head: `756df4a2adb1a38e3617f2774014d1a290e9e105`
  - Decision: routed to topic PRs, not directly imported into integration
  - Primary topic branch: `topic/best-of/plugin-allocation-trims`

## 2026-06-14 sync

Integration base before sync:

- `cfa6566ec207fcc614db9395f7d2d7730a363806`

Imported from `claude/charming-napier-4d62f4`:

- `1c5e79a` -> `74be437e4e1b349f5acc36f5e3c9c1e7b85a426a`: fused interpreter opcode for `(raw + const) % const`
- `75bfdb4` -> `67fe83f4568cd5e8487f132a5ba391fd7b357545`: benchmark ledger update for fused opcode
- `9dc5f14` -> `533b0a2c9bea622555890c562c5eefdd0fafb052`: benchmark ledger feasibility note
- `e36963e` -> `74b842f12ba5212c213c51c8a145211fb70c871d`: f64, nested-loop, and branch-in-loop benchmark probes
- `41d19b2` -> `8109bb1050aa84d7a87ee94095598071cf085191`: general compiled f64 arithmetic unboxing
- `51c3799` -> `061d70930d2adc8c6dd5f7a0da5d28b8e1c0ea3e`: f64 arithmetic loop fast path
- `e69e19f` -> `6aaab73723d739d638d9fdf968c83511db4c5699`: compiled i32 comparison unboxing
- `2d2d789` -> `57149ee84b069bab5199b377afaff4051a796d18`: benchmark ledger update for expanded coverage
- `aea170e` -> `237e2f2377b80ffa805f67f858c3b6bc7fd7812d`: interpreted f64 arithmetic unboxing

Reviewed from `perf/improve-performance1` and routed to topic PRs:

- `0b6d693`: skip audit envelope for no-audit compiled success
- `9f22a12`: use prepared host dispatch for installed kernels
- `e26b1bb`: trim clean plugin message binding work
- `ed1923d`: keep synchronous hook dispatch on the fast path

The `claude/charming-napier-4d62f4` no-finally inline-call experiment remains
outside integration and isolated in `topic/best-of/no-finally-inline-call`.
