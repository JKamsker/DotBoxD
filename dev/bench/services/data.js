window.BENCHMARK_DATA = {
  "lastUpdate": 1784533704970,
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
      },
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
          "id": "442b5e633b4318f70dabec01290e6ba59f590bb1",
          "message": "Merge pull request #811 from JKamsker/codex/value-receiver-host-bindings\n\nSupport class-level host bindings on SDK value objects",
          "timestamp": "2026-07-13T07:37:01Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/442b5e633b4318f70dabec01290e6ba59f590bb1"
        },
        "date": 1783929456653,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 13.600473049614164,
            "unit": "ns",
            "range": "± 0.023454602872521026"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 484.683878686693,
            "unit": "ns",
            "range": "± 2.4177409552749856"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 158.46417371431986,
            "unit": "ns",
            "range": "± 0.6311081308892236"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 12189.119665527343,
            "unit": "ns",
            "range": "± 1420.8806957965214"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 9521.972216796876,
            "unit": "ns",
            "range": "± 1155.1616160402616"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1123065.2953125,
            "unit": "ns",
            "range": "± 40480.6224842141"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 10335220.040625,
            "unit": "ns",
            "range": "± 89934.9273203857"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 62938172.041666664,
            "unit": "ns",
            "range": "± 934438.986739568"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 21.097012529770534,
            "unit": "ns",
            "range": "± 0.08785298733343506"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 37.58642320632934,
            "unit": "ns",
            "range": "± 0.18656712696241437"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 17.47450104728341,
            "unit": "ns",
            "range": "± 0.008475009503675801"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 12.1207238998678,
            "unit": "ns",
            "range": "± 0.008707549284820733"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 23.6420145817101,
            "unit": "ns",
            "range": "± 0.00775838517148718"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 19.22267098352313,
            "unit": "ns",
            "range": "± 0.0124944551731668"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 10.475670254892773,
            "unit": "ns",
            "range": "± 0.017929904281324866"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 4.247718637809157,
            "unit": "ns",
            "range": "± 0.0015815091565046261"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 93.15686382187738,
            "unit": "ns",
            "range": "± 0.035926983370655585"
          }
        ]
      },
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
          "id": "c4d102f9a72be033ebb3f85a25dce565f57187ee",
          "message": "Merge pull request #959 from JKamsker/codex/fix-issue-958\n\nPrevent AD0001 for null array attribute metadata",
          "timestamp": "2026-07-17T11:44:00Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/c4d102f9a72be033ebb3f85a25dce565f57187ee"
        },
        "date": 1784533703927,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 13.07751905620098,
            "unit": "ns",
            "range": "± 0.009580712288789539"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 495.84645144144696,
            "unit": "ns",
            "range": "± 0.711622059368228"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 155.46462784873114,
            "unit": "ns",
            "range": "± 0.27622527370249117"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 11894.797967529297,
            "unit": "ns",
            "range": "± 1184.180434444262"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 8617.740227593316,
            "unit": "ns",
            "range": "± 98.89387477757354"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1124275.27734375,
            "unit": "ns",
            "range": "± 93041.14593553799"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 10253627.275,
            "unit": "ns",
            "range": "± 431764.7464281387"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 60334588.666666664,
            "unit": "ns",
            "range": "± 932812.8070445808"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 38.57432350516319,
            "unit": "ns",
            "range": "± 0.28596770837288477"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 57.22375784516335,
            "unit": "ns",
            "range": "± 0.4496454260349036"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 17.486701801419258,
            "unit": "ns",
            "range": "± 0.02039201029255962"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 12.667349585228497,
            "unit": "ns",
            "range": "± 0.012288188717417403"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 23.610157035291195,
            "unit": "ns",
            "range": "± 0.00915930178445422"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 19.253830870985986,
            "unit": "ns",
            "range": "± 0.008762526809038319"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 10.501365312933922,
            "unit": "ns",
            "range": "± 0.0568821977774229"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 4.25253662019968,
            "unit": "ns",
            "range": "± 0.0037433487130063975"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 93.11030895842447,
            "unit": "ns",
            "range": "± 0.0521953163469118"
          }
        ]
      }
    ]
  }
}