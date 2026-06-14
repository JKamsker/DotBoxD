---
id: PAL-0014
area: perf_alloc
status: open
priority: low
title: IPC convenience defaults bypass the low-allocation profile
dedup_key: alloc/ipc-sharpc/default-options/low-allocation-profile-disabled
created_at: 2026-06-12T22:07:32.7900775+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:07:32.7900775+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0014: IPC convenience defaults bypass the low-allocation profile

## Claim

The SafeIR ShaRPC MessagePack IPC convenience defaults leave the low-allocation unary invocation path disabled, so callers using the named-pipe helpers get the higher-allocation transport profile unless they manually know which ShaRPC options to override.

## Evidence

- `src/SafeIR.Transport.Ipc.ShaRpc/SafeIrShaRpcMessagePackIpc.cs:10` defines `DefaultClientOptions` with `RequestTimeout` and `RejectInboundCalls`, but it does not set `EnableLowAllocationValueTaskInvocations`.
- `src/SafeIR.Transport.Ipc.ShaRpc/SafeIrShaRpcMessagePackIpc.cs:14` defines bidirectional client defaults with only `RequestTimeout`.
- `src/SafeIR.Transport.Ipc.ShaRpc/SafeIrShaRpcMessagePackIpc.cs:23` passes server options through as provided, so default `ListenNamedPipe` callers also do not get the low-allocation server-side options used by the benchmarks.
- `benchmarks/SafeIR.Benchmarks/Ipc/IpcAllocationProfile.cs:76` enables `EnableLowAllocationValueTaskInvocations` only when the explicit `--low-alloc` profile is selected, and `benchmarks/SafeIR.Benchmarks/Ipc/IpcAllocationProfile.cs:89` separately opts the server into low-allocation settings.
- `benchmarks/SafeIR.Benchmarks/Program.cs:8` exposes this only as a manual profile mode; there is no allocation regression gate for the public SafeIR IPC defaults.

## Impact

SafeIR exposes IPC as a preview addon with named-pipe convenience helpers, but the easy path is not the low-allocation path already identified by the benchmark harness. Plugin IPC users can pay avoidable per-call allocation on request/response dispatch while believing they are using the recommended SafeIR transport defaults.

## Better target

Either make the SafeIR IPC defaults use the low-allocation ShaRPC options when the safety tradeoffs are acceptable, or expose an explicit `LowAllocation` SafeIR options factory and document/gate it. The target should make the public convenience path allocation behavior intentional and measured.

## Benchmark/allocation test idea

Add an allocation test or BenchmarkDotNet comparison for `SafeIrShaRpcMessagePackIpc.ListenNamedPipe`/`ConnectNamedPipeAsync` with default options versus a low-allocation options factory. Measure `AddAsync` and struct echo bytes/call, and fail if the documented default/profile regresses beyond an explicit allocation budget.
