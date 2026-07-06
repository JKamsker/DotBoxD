window.BENCHMARK_DATA = {
  "lastUpdate": 1783317425371,
  "repoUrl": "https://github.com/JKamsker/DotBoxD",
  "entries": {
    "DotBoxD.Services Benchmarks": [
      {
        "commit": {
          "author": {
            "name": "Jonas Kamsker",
            "username": "JKamsker",
            "email": "11245306+JKamsker@users.noreply.github.com"
          },
          "committer": {
            "name": "GitHub",
            "username": "web-flow",
            "email": "noreply@github.com"
          },
          "id": "86486247e0564bb27b4304011d2189f3e6c4825d",
          "message": "Add coverage, mutation, banned API, and CodeQL gates\n\nAdd coverage threshold ratchets, focused mutation-test workflows, banned API policy enforcement, and CodeQL workflow guards.\\n\\nThe PR also documents the current coverage and mutation score quality signals, clarifies the conservative Cobertura branch merge behavior, and adds regression coverage for the banned API scanner.\\n\\nClose #484\\nClose #485\\nClose #486\\nClose #487",
          "timestamp": "2026-07-05T15:12:22Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/86486247e0564bb27b4304011d2189f3e6c4825d"
        },
        "date": 1783317424780,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 18.1087949971358,
            "unit": "ns",
            "range": "± 0.01992487648359383"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 479.39742437998456,
            "unit": "ns",
            "range": "± 1.838540984450466"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 167.31126817067465,
            "unit": "ns",
            "range": "± 1.6772558670050566"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 11793.945170084635,
            "unit": "ns",
            "range": "± 3502.096575742949"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 13209.083485921225,
            "unit": "ns",
            "range": "± 4890.49663607949"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 19.113140831391018,
            "unit": "ns",
            "range": "± 0.15473545849371995"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 35.357672403256096,
            "unit": "ns",
            "range": "± 0.13174071140868482"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 19.02457983295123,
            "unit": "ns",
            "range": "± 0.013588114261004059"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 13.36592365304629,
            "unit": "ns",
            "range": "± 0.02435758927496935"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 27.218445787827175,
            "unit": "ns",
            "range": "± 0.13688233922912385"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 20.367484509944916,
            "unit": "ns",
            "range": "± 0.02285367598521379"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 11.926076595981916,
            "unit": "ns",
            "range": "± 0.16298506029487578"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 5.466585809985797,
            "unit": "ns",
            "range": "± 0.0015800655627596528"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 104.65431296825409,
            "unit": "ns",
            "range": "± 0.1906115943827986"
          }
        ]
      }
    ]
  }
}