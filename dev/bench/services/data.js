window.BENCHMARK_DATA = {
  "lastUpdate": 1787548854095,
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
          "id": "289211c6bf33b6f13e4b3c66f33990b0e88bf376",
          "message": "Merge pull request #983 from JKamsker/codex/performance-hunt-20260721\n\nAdd lookahead capacity probe and expand performance coverage",
          "timestamp": "2026-07-24T18:14:20Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/289211c6bf33b6f13e4b3c66f33990b0e88bf376"
        },
        "date": 1785139837508,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 13.475295141339302,
            "unit": "ns",
            "range": "± 0.00586005552252534"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 471.3128154542711,
            "unit": "ns",
            "range": "± 0.2132297064468096"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 155.02162721421985,
            "unit": "ns",
            "range": "± 0.5127064166799672"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 10391.628649902344,
            "unit": "ns",
            "range": "± 1504.0262410288913"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 8801.8962890625,
            "unit": "ns",
            "range": "± 1447.8708759415333"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.RpcTelemetryBenchmarks.SuccessfulRequestWithoutListeners",
            "value": 2.8181572556495667,
            "unit": "ns",
            "range": "± 0.008156845862117571"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1096513.5125,
            "unit": "ns",
            "range": "± 64073.09713626704"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 10205980.869791666,
            "unit": "ns",
            "range": "± 57463.06552736874"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 66763202.76666667,
            "unit": "ns",
            "range": "± 2371968.6025644527"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 19.35841258466244,
            "unit": "ns",
            "range": "± 0.260230912494755"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 35.55638902783394,
            "unit": "ns",
            "range": "± 0.5197231320552295"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 17.741311201453208,
            "unit": "ns",
            "range": "± 0.020707124251505657"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 12.755243764983284,
            "unit": "ns",
            "range": "± 0.02121873515261159"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 23.627054664492608,
            "unit": "ns",
            "range": "± 0.013868748697140103"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 19.40081142485142,
            "unit": "ns",
            "range": "± 0.06066441771111808"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 10.499209970235825,
            "unit": "ns",
            "range": "± 0.01379136232752332"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 4.250230027569665,
            "unit": "ns",
            "range": "± 0.002033920675437492"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 94.12462921275034,
            "unit": "ns",
            "range": "± 0.07358560423055702"
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
          "id": "6f7e8299d53a16479e9cb8cc20b569f127927e12",
          "message": "Merge applicable Dependabot updates (#1001)\n\nAggregate the applicable NuGet, test SDK, and GitHub Actions updates from Dependabot PRs #998, #972, and #875.\\n\\nRoll Meziantou.Analyzer past its false-positive release, retain the compatible Verify.DiffPlex version, regenerate affected gh-aw locks, and align MessagePack consumer pins so the complete CI and package validation matrix remains green.",
          "timestamp": "2026-07-27T12:10:34Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/6f7e8299d53a16479e9cb8cc20b569f127927e12"
        },
        "date": 1785744394534,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 13.329155766301685,
            "unit": "ns",
            "range": "± 0.010035599784197234"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 441.2676263332367,
            "unit": "ns",
            "range": "± 2.549230550046812"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 157.5802195072174,
            "unit": "ns",
            "range": "± 0.1660687357089922"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 10287.326440429688,
            "unit": "ns",
            "range": "± 1562.5242864698391"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 8728.248052978515,
            "unit": "ns",
            "range": "± 1335.7357472026517"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.RpcTelemetryBenchmarks.SuccessfulRequestWithoutListeners",
            "value": 2.8640986047685146,
            "unit": "ns",
            "range": "± 0.05475020942307058"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1153875.33203125,
            "unit": "ns",
            "range": "± 48013.88962390019"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 10624492.3484375,
            "unit": "ns",
            "range": "± 169835.69046293752"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 67945594.26666667,
            "unit": "ns",
            "range": "± 2345555.2783655557"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 21.45874169766903,
            "unit": "ns",
            "range": "± 0.08642198274841788"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 37.44768342971802,
            "unit": "ns",
            "range": "± 0.4025109001665612"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 17.71790865659714,
            "unit": "ns",
            "range": "± 0.018748620395579035"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 12.192787610822254,
            "unit": "ns",
            "range": "± 0.010441554268135976"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 23.629567470815445,
            "unit": "ns",
            "range": "± 0.014606894532885367"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 19.23558090031147,
            "unit": "ns",
            "range": "± 0.011711223014842677"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 10.453668304615551,
            "unit": "ns",
            "range": "± 0.0031942578969640684"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 4.279315458403693,
            "unit": "ns",
            "range": "± 0.04933169476450882"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 93.84719675117069,
            "unit": "ns",
            "range": "± 0.14890945461209612"
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
            "name": "Jonas Kamsker",
            "username": "JKamsker",
            "email": "11245306+JKamsker@users.noreply.github.com"
          },
          "id": "36e4ee5c405258f8d796c94f0aa629729fe71ee3",
          "message": "Upgrade lighter agentic workflows to GPT-5.6 Terra\n\nRun the smoke test, discovery dispatcher, red-test worker, and fix worker with gpt-5.6-terra at xhigh reasoning instead of gpt-5.5 at high reasoning.\n\nRegenerate their gh-aw lock files so both the agent and threat-detection phases use the requested model configuration.",
          "timestamp": "2026-08-09T19:07:37Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/36e4ee5c405258f8d796c94f0aa629729fe71ee3"
        },
        "date": 1786341747307,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 17.79829130238957,
            "unit": "ns",
            "range": "± 0.00933599192182659"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 441.50329542160034,
            "unit": "ns",
            "range": "± 0.21263676850994281"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 165.80821353197098,
            "unit": "ns",
            "range": "± 0.031924639663965984"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 8894.185083007813,
            "unit": "ns",
            "range": "± 1276.637443299412"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 6915.939056396484,
            "unit": "ns",
            "range": "± 241.92558053863738"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.RpcTelemetryBenchmarks.SuccessfulRequestWithoutListeners",
            "value": 2.598578305542469,
            "unit": "ns",
            "range": "± 0.04748056192873063"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 929877.9828125,
            "unit": "ns",
            "range": "± 4678.532354006444"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 9484459.09375,
            "unit": "ns",
            "range": "± 348040.31067669933"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 62579985.037037045,
            "unit": "ns",
            "range": "± 1369298.6536376171"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 18.504666576782864,
            "unit": "ns",
            "range": "± 0.02046277375918209"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 41.628823240598045,
            "unit": "ns",
            "range": "± 0.05268366641071443"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 19.397318813204766,
            "unit": "ns",
            "range": "± 0.02443962326102173"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 13.325577944517136,
            "unit": "ns",
            "range": "± 0.015891779328048947"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 27.009593963623047,
            "unit": "ns",
            "range": "± 0.02043680299738075"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 20.323244631290436,
            "unit": "ns",
            "range": "± 0.003244458394587628"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 11.84417108198007,
            "unit": "ns",
            "range": "± 0.00618130582653022"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 5.438774055242538,
            "unit": "ns",
            "range": "± 0.00866164668003911"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 104.89155173301697,
            "unit": "ns",
            "range": "± 0.0319637570044911"
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
          "id": "b7a733ff8aca5982e8711ad8cc2e3e7dbd608104",
          "message": "Merge pull request #1104 from JKamsker/codex/merge-sweep-fixed-20260815\n\nIntegrate sweep-fixed surprise fixes (2026-08-15)",
          "timestamp": "2026-08-15T12:22:54Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/b7a733ff8aca5982e8711ad8cc2e3e7dbd608104"
        },
        "date": 1786943800092,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 17.89395440220833,
            "unit": "ns",
            "range": "± 0.009365454319065162"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 434.3457273840904,
            "unit": "ns",
            "range": "± 0.18495786296658956"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 166.43060821294785,
            "unit": "ns",
            "range": "± 0.08338693615031591"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 8219.235337999133,
            "unit": "ns",
            "range": "± 211.13733615630053"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 7173.797416687012,
            "unit": "ns",
            "range": "± 143.51108274546465"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.RpcTelemetryBenchmarks.SuccessfulRequestWithoutListeners",
            "value": 2.5266855858266353,
            "unit": "ns",
            "range": "± 0.0027036723974499547"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1234859.5859375,
            "unit": "ns",
            "range": "± 87631.62149643805"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 28996994.833333332,
            "unit": "ns",
            "range": "± 360890.5735876541"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 484954102.9,
            "unit": "ns",
            "range": "± 1935202.5488253995"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 18.7118465701739,
            "unit": "ns",
            "range": "± 0.027434065441885154"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 34.27293512225151,
            "unit": "ns",
            "range": "± 0.17905037283516861"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 19.457852602005005,
            "unit": "ns",
            "range": "± 0.04293322543328686"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 13.318913532627953,
            "unit": "ns",
            "range": "± 0.01718925956994828"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 27.024165573716164,
            "unit": "ns",
            "range": "± 0.017936490026788512"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 20.325154427438974,
            "unit": "ns",
            "range": "± 0.006290316517268049"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 11.84596609738138,
            "unit": "ns",
            "range": "± 0.007711869360124374"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 5.448527459055185,
            "unit": "ns",
            "range": "± 0.01103549465248231"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 104.27813039223354,
            "unit": "ns",
            "range": "± 0.048785126851310694"
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
          "id": "1e689eb9e1080175bbf552b765a3736d7c6aef7e",
          "message": "Merge pull request #1208 from JKamsker/codex/sweep-fixed-20260823\n\nIntegrate fixed surprise sweep",
          "timestamp": "2026-08-23T14:50:47Z",
          "url": "https://github.com/JKamsker/DotBoxD/commit/1e689eb9e1080175bbf552b765a3736d7c6aef7e"
        },
        "date": 1787548853404,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.ParseFrameOnly",
            "value": 13.344282180070877,
            "unit": "ns",
            "range": "± 0.011021642285819387"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.FrameRequest",
            "value": 434.9297725359599,
            "unit": "ns",
            "range": "± 0.4937843164362853"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.FramingBenchmarks.DeserializeArgument",
            "value": 155.1277367538876,
            "unit": "ns",
            "range": "± 0.2506695784701038"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: False)",
            "value": 9891.086279296875,
            "unit": "ns",
            "range": "± 550.0084341386889"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.PeerRoundTripBenchmarks.MovePlayerAsync(EndToEndLowAllocationProfile: True)",
            "value": 9427.175823974609,
            "unit": "ns",
            "range": "± 1345.7718302676753"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.RpcTelemetryBenchmarks.SuccessfulRequestWithoutListeners",
            "value": 2.813513368368149,
            "unit": "ns",
            "range": "± 0.0021270267339120416"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 10)",
            "value": 1414469.06875,
            "unit": "ns",
            "range": "± 14280.824296960278"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 100)",
            "value": 30545537.959375,
            "unit": "ns",
            "range": "± 221690.13392301046"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ServiceGeneratorScaleBenchmarks.RunGenerators(ContractCount: 500)",
            "value": 521052445.4,
            "unit": "ns",
            "range": "± 2017488.5005561411"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.SingleStreamUpload",
            "value": 21.896299669146536,
            "unit": "ns",
            "range": "± 0.048968843935464815"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.StreamedArgumentProxyBenchmarks.TwoStreamUpload",
            "value": 23.238711974024774,
            "unit": "ns",
            "range": "± 0.046662878705549804"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.RegisterPlayerFlow",
            "value": 17.728730854060913,
            "unit": "ns",
            "range": "± 0.017555168854457067"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.GetPlayerStateFlow",
            "value": 12.183753389120103,
            "unit": "ns",
            "range": "± 0.015178121012313604"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MovePlayerFlow",
            "value": 23.629125651386047,
            "unit": "ns",
            "range": "± 0.016243991671176863"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.PerformActionFlow",
            "value": 19.226755622360443,
            "unit": "ns",
            "range": "± 0.004992253212475921"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.MissingPlayerFailureFlow",
            "value": 10.48810855448246,
            "unit": "ns",
            "range": "± 0.013218826695704832"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.VoidHeartbeatFlow",
            "value": 4.248872108757496,
            "unit": "ns",
            "range": "± 0.0029928537208586447"
          },
          {
            "name": "DotBoxD.Services.Benchmarks.Benchmarks.ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow",
            "value": 93.53162174754672,
            "unit": "ns",
            "range": "± 0.029970101773582652"
          }
        ]
      }
    ]
  }
}