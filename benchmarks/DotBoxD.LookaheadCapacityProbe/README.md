# Receive lookahead capacity probe

This standalone probe measures a persistent framed reader over real named pipes and loopback TCP.
It reports latency, managed allocation, `Stream.ReadAsync` calls, pending reads, and pending logical
receives for gated, burst, and fragmented traffic.

Run the practical smoke matrix:

```bash
dotnet run -c Release --project benchmarks/DotBoxD.LookaheadCapacityProbe -- --quick
```

Filter a confirmation run with `--named-pipe` or `--tcp`, one of `--gated`, `--burst`, or
`--fragmented`, and comma-separated values such as `--frame=1024,262144` and
`--capacity=4096,16384,32768,65536`. `--scale=N` multiplies the measured batch count.

The July 2026 sweep rejected 64 KiB despite its coalesced throughput: three 1 GiB-per-capacity TCP
runs over 256 KiB frames measured an 8.7% regression versus the four-byte control. A 32 KiB
follow-up regressed that lane by 6.2%. The selected 16 KiB capacity stayed within the 5% large-frame
guard while substantially reducing reads and latency for small burst traffic. It is a Pareto choice,
not within 3% of the fastest capacity in every individual lane.
