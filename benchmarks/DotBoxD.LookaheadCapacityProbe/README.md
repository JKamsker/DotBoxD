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

The July 2026 raw-reader sweep exposed the capacity tradeoff: three 1 GiB-per-capacity TCP runs over
256 KiB frames moved +8.7% at 64 KiB versus the four-byte control, and a 32 KiB follow-up moved
+6.2%. These experiments guided the bound; they do not model the final adaptive production reader.
The selected 16 KiB window is the smallest tested capacity that delivered the small-batch wins while
also bounding worst-case retained carry.

Production starts every idle receive with an exact four-byte prefix and rents the window only for a
small frame whose prefix completed synchronously. A pending prefix disables lookahead for that frame,
and frames larger than 16 KiB read their body directly. An exact-body miss returns the drained rental
and backs off for 255 frames; actual unread carry keeps lookahead active until consumed. A 1,000-pair
idle-footprint probe retained zero lookahead windows for both named pipes and TCP. Five alternating
production runs over pending 256 KiB frames improved medians by 4.5% for direct named pipes and
3.3% for TCP.
