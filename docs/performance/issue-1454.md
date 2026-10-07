# Issue #1454: query, streaming, subscription, and collection experiments

Four internal optimizations are retained. A standalone primitive-packing adapter is rejected.
The primary claims are exact allocation reductions, fewer frames/credit acquisitions, and
replacement of repeated linear membership scans with prepared lookups. Local timings are
reported as focused probe evidence, not as network throughput or mixed-RPC latency guarantees.

## Baseline and reproduction

Baseline: current `main` at `56c8a1fd50e589b525bca7609f3c5b02ba5af6f2`, with only the
benchmark commit `566890c84` applied. The final implementation uses the same probe code.
Machine: Ubuntu 24.04 x64, Ryzen 9 3900X, SDK 10.0.400. Query/collection probes run on
.NET 10.0.11; Services probes run on .NET 8. Timing on this shared machine is variable.

```bash
git worktree add --detach /tmp/dotboxd-1454-baseline 56c8a1fd50e589b525bca7609f3c5b02ba5af6f2
git -C /tmp/dotboxd-1454-baseline cherry-pick 566890c84
dotnet build benchmarks/DotBoxD.Kernels.Benchmarks -c Release
dotnet build benchmarks/DotBoxD.Services.Benchmarks -c Release
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -- --probe-query-specialization
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -- --probe-query-membership
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -- --probe-query-membership-linear
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -- --probe-query-churn
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -- --probe-primitive-packing
DOTNET_TieredCompilation=0 dotnet run --no-build -c Release --project benchmarks/DotBoxD.Services.Benchmarks -- --probe-buffered-pipe
```

Run the builds/probes from both checkouts. Query/membership/packing probes report five
samples; churn reports three, with one sample at 10,000 to bound baseline run time.
Allocations are measured with `GC.GetAllocatedBytesForCurrentThread`; setup/promotion is
excluded from predicate measurements and included in churn and buffered-pipe transfers.

Pipe and existing-dispatch controls additionally use six fresh process pairs in alternating
BC/CB order, pinned to CPU 11 with `taskset -c 11`, and tiering disabled. Their tables below
report medians across the six process medians. Raw outputs, including all sample ranges,
are committed in [the evidence directory](issue-1454).

The churn baseline was captured before the snapshot optimization, with the query compiler
changes already present. Those changes do not participate in these registration/removal
workloads: they use Compare filters and never publish/promote. This isolates snapshot cost.

## 1. Declared member access and scalar comparisons

Promotion resolves a declared path once and emits public property/field access, storing each
member value once. Getter failures still become null, and comparison runs outside the getter
catch. Value-type roots unbox by address to preserve mutating-getter state. Null/incompatible
targets, unresolved paths, runtime-type readers, and nullable intermediates retain the reader
fallback. Timestamp and other unsupported scalar comparisons retain the existing comparer.

Compatible numeric comparisons preserve exact decimal versus floating domains, full unsigned
range, enums, nullable values, NaN behavior, and fractional/out-of-range bounds. Boolean, GUID,
and ordinal/ordinal-ignore-case string equality also specialize without changing comparers.
No public API or wire representation is added.

| Workload | Before B/op | After B/op | Observed ns/op before → after |
| --- | ---: | ---: | ---: |
| Shallow scalar, compiled hit | 24 | 0 | 170.4 → 4.4 |
| Nested scalar, compiled hit | 24 | 0 | 126.1 → 4.5 |
| Shallow hit, 1,000 subscribers/publish | 24,000 | 0 | 201,862.9 → 60,684.7 |
| Nested hit, 1,000 subscribers/publish | 24,000 | 0 | 217,194.7 → 66,427.0 |
| Runtime-root compiled fallback | 24 | 24 | 107.9 → 105.3 |

[Before](issue-1454/query-before.txt) and [after](issue-1454/query-after.txt) also cover
misses, 1/100/1,000 subscribers, and interpreted controls. Interpreted allocation stays 24 B.
Promotion still occurs at evaluation 16; larger expression trees add one-time compilation
work. These measurements concern already-promoted queries, not promotion/startup speed.

## 2. Prepared IN membership

At compilation, privately owned factory snapshots with at least eight candidates can prepare
ordinal strings, GUIDs, or canonical exact numeric sets. Exact numerics use a decimal set;
floating runtime members use a double set projected from the original literals before
exact-value deduplication. This preserves distinct floating projections of value-equal
decimal literals with different scales. Small lists, mixed literal kinds,
timestamps, Number sets, mutable raw initializers, and custom IConvertible values stay linear.
String/GUID first hits retain a direct first-candidate check. Lookup storage is built once
and never mutated after publication.

The separate linear probe uses the same specialized member access while replacing Values
with an unowned array. It isolates lookup preparation from member-access specialization.

| Eight candidates | Linear ns/op | Prepared ns/op |
| --- | ---: | ---: |
| Integer miss / first / middle / last | 253.5 / 37.3 / 178.7 / 290.6 | 24.1 / 24.3 / 27.1 / 27.0 |
| String miss / first / middle / last | 159.8 / 31.6 / 120.5 / 179.7 | 28.3 / 22.0 / 40.8 / 40.5 |

Eight is a demonstrated conservative crossover; 1/4 stay linear. With 256 candidates, the
original promoted integer/string misses take 10,616.6/8,053.7 ns versus 24.7/29.0 ns prepared.
Every row in this object-member probe stays at 0 B/op. A primitive member can still require
one box for the object comparer; this change removes repeated scans, not all IN boxing.

Raw [baseline](issue-1454/in-before.txt), [prepared](issue-1454/in-after.txt), and
[specialized linear control](issue-1454/in-linear-control.txt) cover 1/4/8/32/64/256 candidates
and miss/first/middle/final hits. One-time hash-set allocation is excluded from these rows.

## 3. Already-buffered pipe segments

When the first available segment is at most 4 KiB and the read already contains multiple
segments, send up to 64 KiB directly from the sequence into the owned outgoing frame. There
is one payload copy, no intermediate coalescing buffer, and no read to wait for padding.
Larger segments use the contiguous-memory path. A failed send advances only past successful
frames; unsent bytes remain readable for borrowed pipes.

Each transfer contains exactly 1 MiB of verified bytes. Frame counts also equal credit
acquisitions, because SendStreamItemAsync acquires credit once per frame. Allocations include
pipe population and setup, which account for most of the remaining bytes.

| Producer segment | Frames/credit acquisitions before → after | B/transfer before → after | Local µs/transfer before → after |
| --- | ---: | ---: | ---: |
| 256 B | 4,096 → 16 | 1,807,448 → 1,676,888 | 1877.70 → 696.10 |
| 1,024 B | 1,024 → 16 | 1,242,200 → 1,209,944 | 664.35 → 314.10 |
| 4,096 B | 256 → 16 | 1,100,888 → 1,093,208 | 322.60 → 204.30 |
| 16,384 B | 64 → 64 | 1,062,440 → 1,062,440 | 228.70 → 182.15 |
| 65,536 B | 16 → 16 | 1,052,792 → 1,052,792 | 157.75 → 141.85 |

The [initial experiment](issue-1454/pipe-rejected-all-segments-after-pair1.txt) coalesced all segment sizes. Its larger-segment timing did not give
a consistent win, so it was narrowed to <=4 KiB. The final 16/64 KiB controls keep exact
allocations and frame counts, and neither median regresses across the six balanced pairs.
The small-segment results demonstrate reduced send/credit work and allocations. A real
transport, CPU profile, resident memory, and unary latency under streaming contention were
not measured; this PR makes no claim about those metrics.

## 4. Reuse subscription routing snapshots

Registration/removal copies only the affected routing group's dictionary and bucket. Other
groups, paths, and buckets are reused. Broad subscriptions update only their array. No
published array, dictionary, or bucket is mutated, so publish retains its stable lock-free
snapshot. Registration order preserves the previous group order after removing a group's
earliest entry; bucket order and broad-before-routed dispatch also remain intact.

| 10,000 subscriptions | Registration allocation before → after | Removal allocation before → after |
| --- | ---: | ---: |
| Shared routing shape | 40,022,917,776 → 1,579,638,416 B | 40,051,572,832 → 1,543,055,696 B |
| 31 routing shapes | 69,945,458,816 → 155,563,072 B | 70,233,869,496 → 70,854,744 B |
| Broad | 2,067,787,472 → 474,066,272 B | 1,995,592,816 → 402,242,952 B |
| Mixed | 35,976,106,840 → 202,092,864 B | 36,094,016,232 → 123,972,472 B |

The 31-shape registration allocation drops 99.78%. Observed setup/removal wall times are in
the [baseline](issue-1454/churn-before.txt) and [candidate](issue-1454/churn-after.txt), with
10/100/1,000/10,000 coverage. The large baseline times include unrelated machine load;
allocation, not a precise setup-speed multiplier, is the primary claim.

This does not make all churn asymptotically linear: broad arrays and a touched group's
dictionary/bucket still copy, and earliest-entry removal scans bucket heads for group order.
The improvement removes repeated rebuilding of every entry's sorted paths and composite keys,
and avoids revisiting unrelated groups. Bulk registration/persistent maps remain possible
future work; they are not added to the public API here.

Existing dispatch controls, six balanced pairs:

| Control | B/publish before → after | Observed ns/publish before → after |
| --- | ---: | ---: |
| Broad single subscriber | 80 → 80 | 171.80 → 169.75 |
| Indexed hit | 168 → 168 | 394.90 → 362.65 |
| Indexed miss | 88 → 88 | 190.25 → 185.05 |

Every control retains exact allocation and has no median regression. These are dispatch
controls for snapshot sharing, not separate performance claims.

## 5. Rejected primitive-packing adapter

An immutable owned long[] adapter prototypes packed I64 storage and lazily exposes
SandboxValue wrappers through IReadOnlyList. It is benchmark-only and is not used by the
sandbox, compiler, interpreter, validation, metering, or RPC paths.

At 4,096 elements:

| Operation | Generic representation | Packing prototype |
| --- | ---: | ---: |
| Construct storage | 163,952 B | 32,816 B |
| One complete indexed sum | 0 B | 98,304 B |
| Construct + public ListValue materialization | 163,952 B | 262,320 B |

Packing alone saves about 80% of construction allocation, but every indexed read recreates
24 B wrappers. The existing public-list boundary validates and snapshots those wrappers:
packing plus materialization allocates 60% more than the current construction control.
The adapter is rejected for these allocation regressions. [Raw results](issue-1454/packing.txt)
cover 32/256/4,096/65,536 elements and verify identical sums.

This does not disprove packing carried through typed runtime/RPC operations. That broader
change needs first-class primitive access and dedicated ownership, fuel, resource-accounting,
validation-order, malformed-input, and compiler/interpreter parity proofs. No end-to-end
packing benefit is claimed from this storage-only prototype.

## Correctness and validation

Dedicated controls cover declared interface/base/hidden members, virtual dispatch, fields,
getter faults, null propagation, one getter invocation, boxed mutating structs, scalar domains,
nullable/enum/NaN/timestamp/comparer fallback semantics, IN duplicates and mutable candidates,
allocation-free promoted scalar evaluation, concurrent publication versus registration/removal,
dispatch ordering, bounded stream chunks, borrowed/owned failure cleanup, cancellation/credit
failure, byte ordering, and sending a short buffer before writer completion.

Local validation includes the warnings-as-errors Release solution build, all Queryable tests,
the full Services suite, Architecture tests, formatting, file/folder guards, and public API
baselines. The PR also runs the complete repository CI suites and quality gates.
