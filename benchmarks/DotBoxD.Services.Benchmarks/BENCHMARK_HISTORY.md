# DotBoxD Services Benchmark History

All results below are local stopwatch probes on this machine, run in Release mode.

## Commands

```text
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-peer-proxy-cache
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-stream-connection-receive-tracking
```

## Ledger

| Change | Commit | Probe | Result |
| --- | --- | --- | --- |
| Root RPC proxy cache | this commit | `--probe-peer-proxy-cache` | Repeated locked proxy creation measured 33.2 ms / 32,000,040 B for 1,000,000 calls; cached `RpcPeer.Get<IGameService>` measured 34.6 ms / 40 B. This is an allocation-only win for the small root proxy in the probe, with registry-stamp invalidation preserving replacement registration behavior. |
| Owned stream receive tracking | this commit | `--probe-stream-connection-receive-tracking` | Owned empty-stream receives with legacy active-receive tracking simulated around the current body measured 124.2 ms / 72,001,584 B for 1,000,000 calls; current owned receives measured 113.8 ms / 72,000,040 B. Non-owned streams still track active receives for blocked-read shutdown. |
