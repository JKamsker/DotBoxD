# DotBoxD Services Performance Findings

All numbers are local Release stopwatch probes on the same machine and are intended
to keep optimization claims concrete.

| Finding | Probe | Workload | Before Total | Before ns/op | Before Alloc | Before B/op | After Total | After ns/op | After Alloc | After B/op | Notes |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Repeated `RpcPeer.Get<TService>()` calls created a fresh generated root proxy. | `--probe-peer-proxy-cache` | 1,000,000 locked legacy proxy creations vs cached `RpcPeer.Get<IGameService>` calls | 33.2 ms | 33.2 | 32,000,040 B | 32.0 | 34.6 ms | 34.6 | 40 B | ~0 | Allocation-only improvement for the small root proxy in this probe; cache entries are invalidated by generated-registry registration stamps. |
| Owned `StreamConnection.ReceiveAsync` paid active-receive interlocked operations that only protect non-owned stream disposal. | `--probe-stream-connection-receive-tracking` | 1,000,000 owned empty-stream receives, with legacy tracking simulated around the current receive body | 124.2 ms | 124.2 | 72,001,584 B | 72.0 | 113.8 ms | 113.8 | 72,000,040 B | 72.0 | Time-only improvement; non-owned streams still track active receives so blocked reads are disposed during peer shutdown. |

## Commands

```text
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-peer-proxy-cache
dotnet run -c Release --project benchmarks/DotBoxD.Services.Benchmarks -p:UseSharedCompilation=false -- --probe-stream-connection-receive-tracking
```
