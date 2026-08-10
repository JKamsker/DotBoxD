window.BENCHMARK_DATA = {
  "lastUpdate": 1786341750757,
  "repoUrl": "https://github.com/JKamsker/DotBoxD",
  "entries": {
    "DotBoxD.Kernels Benchmarks": [
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
        "date": 1783929459964,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 1)",
            "value": 32984.12654622396,
            "unit": "ns",
            "range": "± 393.80922689701185"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 10)",
            "value": 69627.72200520833,
            "unit": "ns",
            "range": "± 7368.992104796495"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 100)",
            "value": 551666.71875,
            "unit": "ns",
            "range": "± 63182.06987370271"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 100)",
            "value": 3282.0359789530435,
            "unit": "ns",
            "range": "± 1.6012856461885527"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 100)",
            "value": 2706.5,
            "unit": "ns",
            "range": "± 32.357379374726875"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 1000)",
            "value": 35286.52824910482,
            "unit": "ns",
            "range": "± 208.9184244130902"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 1000)",
            "value": 2763.1666666666665,
            "unit": "ns",
            "range": "± 51.86842327788009"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 10000)",
            "value": 650723.5289713541,
            "unit": "ns",
            "range": "± 32695.663172166915"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 10000)",
            "value": 3049.6666666666665,
            "unit": "ns",
            "range": "± 168.46463526014395"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1)",
            "value": 52793,
            "unit": "ns",
            "range": "± 2958.1999594347913"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 10)",
            "value": 187084.66666666666,
            "unit": "ns",
            "range": "± 18329.746052068844"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1000)",
            "value": 11298394.833333334,
            "unit": "ns",
            "range": "± 193258.96932699744"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1)",
            "value": 51774.166666666664,
            "unit": "ns",
            "range": "± 3613.4432793850983"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 10)",
            "value": 163796.66666666666,
            "unit": "ns",
            "range": "± 4684.282264481223"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1000)",
            "value": 12108424,
            "unit": "ns",
            "range": "± 264049.64633757796"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1)",
            "value": 79013.83333333333,
            "unit": "ns",
            "range": "± 17545.830112403724"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 10)",
            "value": 182202.83333333334,
            "unit": "ns",
            "range": "± 22917.517892069667"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1000)",
            "value": 12261896.166666666,
            "unit": "ns",
            "range": "± 37567.314747441465"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1)",
            "value": 99104.5,
            "unit": "ns",
            "range": "± 2956.247452430192"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 10)",
            "value": 1165469.3333333333,
            "unit": "ns",
            "range": "± 12887.686539225468"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1000)",
            "value": 66981143.833333336,
            "unit": "ns",
            "range": "± 554285.3317104228"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 100)",
            "value": 5744.288040161133,
            "unit": "ns",
            "range": "± 24.078831182094763"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 10000)",
            "value": 58384.00478108724,
            "unit": "ns",
            "range": "± 163.54892529021654"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 11289.849426269531,
            "unit": "ns",
            "range": "± 3106.358153757385"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 12689.808319091797,
            "unit": "ns",
            "range": "± 4877.252509761021"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 9172.572428385416,
            "unit": "ns",
            "range": "± 1691.2264696371699"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 11373.29502360026,
            "unit": "ns",
            "range": "± 2900.1939655339656"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 67960.47249348958,
            "unit": "ns",
            "range": "± 4204.826604131857"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 67977.10286458333,
            "unit": "ns",
            "range": "± 11629.749352429015"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 62441.46590169271,
            "unit": "ns",
            "range": "± 5487.12687850874"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 56806.22412109375,
            "unit": "ns",
            "range": "± 1701.2912944760753"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.SerializeStructPayload",
            "value": 53.74439130226771,
            "unit": "ns",
            "range": "± 0.07857268996094467"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.DeserializeStructPayload",
            "value": 60.08595448732376,
            "unit": "ns",
            "range": "± 0.8576328304047606"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Int32)",
            "value": 9.625718702872595,
            "unit": "ns",
            "range": "± 0.008560108933848393"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Int32)",
            "value": 61.097641269365944,
            "unit": "ns",
            "range": "± 0.137598931966265"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Int32)",
            "value": 24.351501554250717,
            "unit": "ns",
            "range": "± 0.027737813951058954"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Int32)",
            "value": 60.23312842845917,
            "unit": "ns",
            "range": "± 0.19597083348960317"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: String)",
            "value": 22.921970466772716,
            "unit": "ns",
            "range": "± 0.017537235974950625"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: String)",
            "value": 92.58240932226181,
            "unit": "ns",
            "range": "± 0.2940333321387192"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: String)",
            "value": 48.898159861564636,
            "unit": "ns",
            "range": "± 0.13730280410753526"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: String)",
            "value": 118.77836684385936,
            "unit": "ns",
            "range": "± 0.34874718343573224"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Enum)",
            "value": 9.98744821548462,
            "unit": "ns",
            "range": "± 0.005190955341369131"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Enum)",
            "value": 60.05091071128845,
            "unit": "ns",
            "range": "± 0.2155282010023347"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Enum)",
            "value": 24.818885306517284,
            "unit": "ns",
            "range": "± 0.031487131316982525"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Enum)",
            "value": 59.585757394631706,
            "unit": "ns",
            "range": "± 0.15036879573340708"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: ListInt32)",
            "value": 66.27066729466121,
            "unit": "ns",
            "range": "± 0.4959245508084519"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: ListInt32)",
            "value": 377.70551840464276,
            "unit": "ns",
            "range": "± 0.5143965329720349"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: ListInt32)",
            "value": 59.28191224733988,
            "unit": "ns",
            "range": "± 0.9550923431579381"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: ListInt32)",
            "value": 441.37539037068683,
            "unit": "ns",
            "range": "± 1.1711195681147004"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Dto)",
            "value": 44.33350890874863,
            "unit": "ns",
            "range": "± 0.047307328591573834"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Dto)",
            "value": 228.4238204161326,
            "unit": "ns",
            "range": "± 2.687861126811683"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Dto)",
            "value": 63.281366765499115,
            "unit": "ns",
            "range": "± 0.2071057546450824"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Dto)",
            "value": 266.31859318415326,
            "unit": "ns",
            "range": "± 1.0966505020282051"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: AnonymousDto)",
            "value": 44.84267549713453,
            "unit": "ns",
            "range": "± 0.6808479447183491"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: AnonymousDto)",
            "value": 430.1012355486552,
            "unit": "ns",
            "range": "± 0.4083962146459254"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: AnonymousDto)",
            "value": 76.01930906375249,
            "unit": "ns",
            "range": "± 0.0914402229027097"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: AnonymousDto)",
            "value": 514.9740708669027,
            "unit": "ns",
            "range": "± 0.9356841150305827"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: WholeEvent)",
            "value": 50.48886528611183,
            "unit": "ns",
            "range": "± 0.049182722967259764"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: WholeEvent)",
            "value": 281.54311259587604,
            "unit": "ns",
            "range": "± 0.41929027417001646"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: WholeEvent)",
            "value": 53.30931484699249,
            "unit": "ns",
            "range": "± 0.13864164790371855"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: WholeEvent)",
            "value": 342.659206867218,
            "unit": "ns",
            "range": "± 0.4210457762499899"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: False)",
            "value": 289800.5904947917,
            "unit": "ns",
            "range": "± 922.1556994328919"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: True)",
            "value": 1285601.2604166667,
            "unit": "ns",
            "range": "± 49737.71386892571"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: False)",
            "value": 3121084.2799479165,
            "unit": "ns",
            "range": "± 26211.760798135867"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: True)",
            "value": 94312409,
            "unit": "ns",
            "range": "± 967018.4831233837"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: False)",
            "value": 16607002.78125,
            "unit": "ns",
            "range": "± 132853.17716766495"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: True)",
            "value": 2287556505,
            "unit": "ns",
            "range": "± 22946743.975144688"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 100)",
            "value": 13551311.630208334,
            "unit": "ns",
            "range": "± 2445515.9358516107"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 1000)",
            "value": 98825108.55555557,
            "unit": "ns",
            "range": "± 2598464.8035463938"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 10000)",
            "value": 1275587269.3333333,
            "unit": "ns",
            "range": "± 18949754.229725022"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 10)",
            "value": 619801.4791666666,
            "unit": "ns",
            "range": "± 11226.817155148818"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 100)",
            "value": 2634477.5572916665,
            "unit": "ns",
            "range": "± 5711.18091775925"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 500)",
            "value": 11636288.854166666,
            "unit": "ns",
            "range": "± 75652.69042571943"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.OneProperty",
            "value": 12.783688952525457,
            "unit": "ns",
            "range": "± 0.620297110029151"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.FiveProperties",
            "value": 39.084828515847526,
            "unit": "ns",
            "range": "± 0.11218811861829249"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.TwentyProperties",
            "value": 201.9754297733307,
            "unit": "ns",
            "range": "± 5.601457039512383"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 1000)",
            "value": 253951.65364583334,
            "unit": "ns",
            "range": "± 638.2449432022806"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 1000)",
            "value": 285263.19124348956,
            "unit": "ns",
            "range": "± 346.7145886389661"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 100000)",
            "value": 25212672.03125,
            "unit": "ns",
            "range": "± 24652.036954060713"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 100000)",
            "value": 27150128.895833332,
            "unit": "ns",
            "range": "± 51272.679477772595"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 100)",
            "value": 110347.06001790364,
            "unit": "ns",
            "range": "± 312.32028078299373"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 1000)",
            "value": 1088369.1217447917,
            "unit": "ns",
            "range": "± 9684.45558863097"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 10000)",
            "value": 11968003.427083334,
            "unit": "ns",
            "range": "± 323190.5937111438"
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
        "date": 1785139841404,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 1)",
            "value": 34546.045084635414,
            "unit": "ns",
            "range": "± 4802.7910316065"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 10)",
            "value": 69571.01302083333,
            "unit": "ns",
            "range": "± 10218.403823314364"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 100)",
            "value": 481051.8072916667,
            "unit": "ns",
            "range": "± 36500.25324925319"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 100)",
            "value": 3335.267717997233,
            "unit": "ns",
            "range": "± 2.5442534191846735"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 100)",
            "value": 2885.6666666666665,
            "unit": "ns",
            "range": "± 106.2089136246734"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 1000)",
            "value": 32434.015279134113,
            "unit": "ns",
            "range": "± 180.18364396561037"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 1000)",
            "value": 3097.5,
            "unit": "ns",
            "range": "± 515.2931204664002"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 10000)",
            "value": 583043.46875,
            "unit": "ns",
            "range": "± 20162.231909956947"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 10000)",
            "value": 3539.3333333333335,
            "unit": "ns",
            "range": "± 166.5332799572906"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1)",
            "value": 62514.166666666664,
            "unit": "ns",
            "range": "± 14669.733205935729"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 10)",
            "value": 155477.66666666666,
            "unit": "ns",
            "range": "± 14911.820154941961"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1000)",
            "value": 10670567,
            "unit": "ns",
            "range": "± 31583.547125045978"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1)",
            "value": 50454.5,
            "unit": "ns",
            "range": "± 43.58898943540674"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 10)",
            "value": 143936.33333333334,
            "unit": "ns",
            "range": "± 1492.349936621211"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1000)",
            "value": 10948979,
            "unit": "ns",
            "range": "± 133326.8333419796"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1)",
            "value": 51397.833333333336,
            "unit": "ns",
            "range": "± 480.07534130939626"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 10)",
            "value": 180285.33333333334,
            "unit": "ns",
            "range": "± 23483.502982590424"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1000)",
            "value": 11755872.666666666,
            "unit": "ns",
            "range": "± 117932.12248718893"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1)",
            "value": 89247.66666666667,
            "unit": "ns",
            "range": "± 1006.2724945725851"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 10)",
            "value": 1269497.3333333333,
            "unit": "ns",
            "range": "± 214093.23326376604"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1000)",
            "value": 63897732.666666664,
            "unit": "ns",
            "range": "± 289307.5419019237"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 100)",
            "value": 21047.133728027344,
            "unit": "ns",
            "range": "± 15.958906112209855"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 10000)",
            "value": 59139.33504231771,
            "unit": "ns",
            "range": "± 58.83652549712261"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 12170.662913004557,
            "unit": "ns",
            "range": "± 1884.2936168506114"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 10406.557332356771,
            "unit": "ns",
            "range": "± 1717.9268498334245"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 12131.041259765625,
            "unit": "ns",
            "range": "± 60.98870189228249"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 10823.797190348307,
            "unit": "ns",
            "range": "± 3516.398542666107"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 49718.9491780599,
            "unit": "ns",
            "range": "± 947.4798544271058"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 64815.507080078125,
            "unit": "ns",
            "range": "± 7229.9160530603995"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 47679.50459798177,
            "unit": "ns",
            "range": "± 408.4428338893801"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 47671.07627360026,
            "unit": "ns",
            "range": "± 440.29503164520673"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.SerializeStructPayload",
            "value": 42.65458460648855,
            "unit": "ns",
            "range": "± 0.014760285441187132"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.DeserializeStructPayload",
            "value": 57.68820955355962,
            "unit": "ns",
            "range": "± 0.02299777555711828"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Int32)",
            "value": 9.633878752589226,
            "unit": "ns",
            "range": "± 0.014837428700093195"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Int32)",
            "value": 50.698847572008766,
            "unit": "ns",
            "range": "± 0.02416959120959287"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Int32)",
            "value": 21.355493744214375,
            "unit": "ns",
            "range": "± 0.0055487886800095285"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Int32)",
            "value": 55.0237038731575,
            "unit": "ns",
            "range": "± 0.08406268006394171"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: String)",
            "value": 22.95258727669716,
            "unit": "ns",
            "range": "± 0.03960683692003344"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: String)",
            "value": 95.7326091726621,
            "unit": "ns",
            "range": "± 0.2139515395803465"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: String)",
            "value": 46.05330206950506,
            "unit": "ns",
            "range": "± 0.16118402611096286"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: String)",
            "value": 118.35599223772685,
            "unit": "ns",
            "range": "± 0.30087792186819773"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Enum)",
            "value": 9.626634786526362,
            "unit": "ns",
            "range": "± 0.03494426247787033"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Enum)",
            "value": 50.82380204399427,
            "unit": "ns",
            "range": "± 0.15142523329338523"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Enum)",
            "value": 19.82344577709834,
            "unit": "ns",
            "range": "± 0.1071580005690204"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Enum)",
            "value": 55.22651787598928,
            "unit": "ns",
            "range": "± 0.06476512766428646"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: ListInt32)",
            "value": 59.704211592674255,
            "unit": "ns",
            "range": "± 0.03341640543068017"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: ListInt32)",
            "value": 337.96657609939575,
            "unit": "ns",
            "range": "± 0.24450943607819572"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: ListInt32)",
            "value": 53.00055482983589,
            "unit": "ns",
            "range": "± 0.4284094495305792"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: ListInt32)",
            "value": 464.73568391799927,
            "unit": "ns",
            "range": "± 1.48082094889337"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Dto)",
            "value": 44.52953040599823,
            "unit": "ns",
            "range": "± 0.061304525102206985"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Dto)",
            "value": 232.06443436940512,
            "unit": "ns",
            "range": "± 0.5785170551949577"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Dto)",
            "value": 59.8580459356308,
            "unit": "ns",
            "range": "± 0.2018788972446022"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Dto)",
            "value": 275.77432521184284,
            "unit": "ns",
            "range": "± 2.534584942634552"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: AnonymousDto)",
            "value": 44.186767826477684,
            "unit": "ns",
            "range": "± 0.061407912681814295"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: AnonymousDto)",
            "value": 418.9172968864441,
            "unit": "ns",
            "range": "± 2.5885639068072948"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: AnonymousDto)",
            "value": 78.98278552293777,
            "unit": "ns",
            "range": "± 0.41404464750210057"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: AnonymousDto)",
            "value": 484.17371400197345,
            "unit": "ns",
            "range": "± 1.5576888522260013"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: WholeEvent)",
            "value": 50.85050365328789,
            "unit": "ns",
            "range": "± 0.10371819870459213"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: WholeEvent)",
            "value": 285.5386633872986,
            "unit": "ns",
            "range": "± 0.8445142080405311"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: WholeEvent)",
            "value": 60.39009610811869,
            "unit": "ns",
            "range": "± 0.21537444231158073"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: WholeEvent)",
            "value": 336.8381573359172,
            "unit": "ns",
            "range": "± 0.34567454949748355"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: False)",
            "value": 280816.3287760417,
            "unit": "ns",
            "range": "± 2211.264098124166"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: True)",
            "value": 1164541.7317708333,
            "unit": "ns",
            "range": "± 2810.2682646102526"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: False)",
            "value": 3054705.2747395835,
            "unit": "ns",
            "range": "± 26334.40339425275"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: True)",
            "value": 88746435.16666667,
            "unit": "ns",
            "range": "± 729243.0642636125"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: False)",
            "value": 15496695.197916666,
            "unit": "ns",
            "range": "± 548536.714006763"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: True)",
            "value": 2125965746.3333333,
            "unit": "ns",
            "range": "± 7498860.828251271"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 100)",
            "value": 13100463.177083334,
            "unit": "ns",
            "range": "± 3524195.5522030327"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 1000)",
            "value": 115269160.55555554,
            "unit": "ns",
            "range": "± 5781537.495342582"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 10000)",
            "value": 1264317377.6666667,
            "unit": "ns",
            "range": "± 22662703.137543"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 10)",
            "value": 594464.716796875,
            "unit": "ns",
            "range": "± 14309.897078672733"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 100)",
            "value": 2555058.8645833335,
            "unit": "ns",
            "range": "± 37909.02676187174"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 500)",
            "value": 10586343.442708334,
            "unit": "ns",
            "range": "± 17799.454633618185"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.OneProperty",
            "value": 11.51790569225947,
            "unit": "ns",
            "range": "± 0.01651690297886936"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.FiveProperties",
            "value": 37.4463839729627,
            "unit": "ns",
            "range": "± 0.07570550727910691"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.TwentyProperties",
            "value": 182.9610169728597,
            "unit": "ns",
            "range": "± 0.8859358401199872"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 1000)",
            "value": 252504.7001953125,
            "unit": "ns",
            "range": "± 730.1274845790925"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 1000)",
            "value": 268801.0055338542,
            "unit": "ns",
            "range": "± 1874.7836624114175"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 100000)",
            "value": 23477353.0625,
            "unit": "ns",
            "range": "± 101191.6142332547"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 100000)",
            "value": 28210242.125,
            "unit": "ns",
            "range": "± 6575.226844054983"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 100)",
            "value": 108022.47875976562,
            "unit": "ns",
            "range": "± 934.7549585209122"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 1000)",
            "value": 1079292.0221354167,
            "unit": "ns",
            "range": "± 5932.732581303593"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 10000)",
            "value": 11863051.9375,
            "unit": "ns",
            "range": "± 243370.0552821974"
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
        "date": 1785744413603,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 1)",
            "value": 30252.259847005207,
            "unit": "ns",
            "range": "± 155.68916902377453"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 10)",
            "value": 67724.9287109375,
            "unit": "ns",
            "range": "± 10897.062886152089"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 100)",
            "value": 502685.64453125,
            "unit": "ns",
            "range": "± 49210.77563137655"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 100)",
            "value": 3252.2655283610025,
            "unit": "ns",
            "range": "± 1.860797322702318"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 100)",
            "value": 2950.8333333333335,
            "unit": "ns",
            "range": "± 151.52667531934216"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 1000)",
            "value": 33551.40757242838,
            "unit": "ns",
            "range": "± 536.0345246968632"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 1000)",
            "value": 3945.8333333333335,
            "unit": "ns",
            "range": "± 1871.3592208160712"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 10000)",
            "value": 605249.9401041666,
            "unit": "ns",
            "range": "± 14913.69709992019"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 10000)",
            "value": 3499.6666666666665,
            "unit": "ns",
            "range": "± 215.12864368403697"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1)",
            "value": 55203.166666666664,
            "unit": "ns",
            "range": "± 2896.3201365410787"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 10)",
            "value": 147009.83333333334,
            "unit": "ns",
            "range": "± 2593.8898074770514"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1000)",
            "value": 10607187,
            "unit": "ns",
            "range": "± 168025.89044251485"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1)",
            "value": 52061.666666666664,
            "unit": "ns",
            "range": "± 1197.348877033479"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 10)",
            "value": 168268.16666666666,
            "unit": "ns",
            "range": "± 22610.359801058745"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1000)",
            "value": 10990594.5,
            "unit": "ns",
            "range": "± 69363.63931484564"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1)",
            "value": 53970.833333333336,
            "unit": "ns",
            "range": "± 10065.861728303908"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 10)",
            "value": 152673.83333333334,
            "unit": "ns",
            "range": "± 2205.0923639007356"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1000)",
            "value": 11615909.833333334,
            "unit": "ns",
            "range": "± 40739.70753863279"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1)",
            "value": 108696.16666666667,
            "unit": "ns",
            "range": "± 12788.194256161943"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 10)",
            "value": 1095063.6666666667,
            "unit": "ns",
            "range": "± 5363.032755198623"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1000)",
            "value": 64669776.5,
            "unit": "ns",
            "range": "± 342639.7625261843"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 100)",
            "value": 21143.534220377605,
            "unit": "ns",
            "range": "± 19.926369383599305"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 10000)",
            "value": 62219.13065592448,
            "unit": "ns",
            "range": "± 20.66735969743876"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 7306.2021077473955,
            "unit": "ns",
            "range": "± 647.7171912007537"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 11628.166147867838,
            "unit": "ns",
            "range": "± 931.2719135472091"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 12183.852478027344,
            "unit": "ns",
            "range": "± 444.15802384403486"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 8511.524892171225,
            "unit": "ns",
            "range": "± 1477.2510261253326"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 50530.168131510414,
            "unit": "ns",
            "range": "± 628.602374238147"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 54145.08154296875,
            "unit": "ns",
            "range": "± 414.4651480149858"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 58385.38427734375,
            "unit": "ns",
            "range": "± 3574.8413509952"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 63664.244384765625,
            "unit": "ns",
            "range": "± 9723.159888773278"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.SerializeStructPayload",
            "value": 41.510551035404205,
            "unit": "ns",
            "range": "± 0.16996502807513178"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.DeserializeStructPayload",
            "value": 57.39630897839864,
            "unit": "ns",
            "range": "± 0.03128907588252721"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Int32)",
            "value": 9.636336470643679,
            "unit": "ns",
            "range": "± 0.048506380553025176"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Int32)",
            "value": 50.851847410202026,
            "unit": "ns",
            "range": "± 0.054050696297391794"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Int32)",
            "value": 21.276957829793293,
            "unit": "ns",
            "range": "± 0.8937494108280634"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Int32)",
            "value": 55.64770629008611,
            "unit": "ns",
            "range": "± 0.13483477814961053"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: String)",
            "value": 22.81806356708209,
            "unit": "ns",
            "range": "± 0.011544066016628718"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: String)",
            "value": 95.95688851674397,
            "unit": "ns",
            "range": "± 0.3615933904998202"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: String)",
            "value": 46.81143254041672,
            "unit": "ns",
            "range": "± 0.10911240917774004"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: String)",
            "value": 117.4996988773346,
            "unit": "ns",
            "range": "± 0.46860305197358787"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Enum)",
            "value": 9.811734015742937,
            "unit": "ns",
            "range": "± 0.06466928683500948"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Enum)",
            "value": 51.03586630026499,
            "unit": "ns",
            "range": "± 0.5037588889789621"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Enum)",
            "value": 19.776597797870636,
            "unit": "ns",
            "range": "± 0.04345137027075027"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Enum)",
            "value": 55.65523520112038,
            "unit": "ns",
            "range": "± 0.0069221828070793415"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: ListInt32)",
            "value": 59.937124927838646,
            "unit": "ns",
            "range": "± 0.06544528677227658"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: ListInt32)",
            "value": 344.4631042480469,
            "unit": "ns",
            "range": "± 1.9202986150730645"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: ListInt32)",
            "value": 53.55881452560425,
            "unit": "ns",
            "range": "± 0.15568151194249777"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: ListInt32)",
            "value": 431.8938461939494,
            "unit": "ns",
            "range": "± 2.389496420930803"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Dto)",
            "value": 44.74962924917539,
            "unit": "ns",
            "range": "± 0.08912333852181446"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Dto)",
            "value": 228.3943912188212,
            "unit": "ns",
            "range": "± 1.156674413791549"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Dto)",
            "value": 58.97706260283788,
            "unit": "ns",
            "range": "± 0.18003596092407556"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Dto)",
            "value": 276.96609433492023,
            "unit": "ns",
            "range": "± 0.8026132223736183"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: AnonymousDto)",
            "value": 44.123633613189064,
            "unit": "ns",
            "range": "± 0.04020142495021044"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: AnonymousDto)",
            "value": 452.6212256749471,
            "unit": "ns",
            "range": "± 0.8683203236368375"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: AnonymousDto)",
            "value": 74.26033294200897,
            "unit": "ns",
            "range": "± 0.4039483843250894"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: AnonymousDto)",
            "value": 471.86972363789874,
            "unit": "ns",
            "range": "± 2.7854468192600144"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: WholeEvent)",
            "value": 50.85700731476148,
            "unit": "ns",
            "range": "± 0.11362422485671488"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: WholeEvent)",
            "value": 291.8839602470398,
            "unit": "ns",
            "range": "± 1.4824313101847897"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: WholeEvent)",
            "value": 63.193798422813416,
            "unit": "ns",
            "range": "± 0.23671157556829805"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: WholeEvent)",
            "value": 344.14790503184,
            "unit": "ns",
            "range": "± 3.787097230146342"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: False)",
            "value": 287539.5095214844,
            "unit": "ns",
            "range": "± 1069.0316722588811"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: True)",
            "value": 1232402.8971354167,
            "unit": "ns",
            "range": "± 54530.386982681295"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: False)",
            "value": 3174792.8645833335,
            "unit": "ns",
            "range": "± 2217.8938030839113"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: True)",
            "value": 95293543.44444443,
            "unit": "ns",
            "range": "± 1260278.7002758598"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: False)",
            "value": 16309457.614583334,
            "unit": "ns",
            "range": "± 568232.4981326145"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: True)",
            "value": 2172802093.6666665,
            "unit": "ns",
            "range": "± 12776041.29863223"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 100)",
            "value": 17304237.223958332,
            "unit": "ns",
            "range": "± 4467582.4285550155"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 1000)",
            "value": 115267777.55555554,
            "unit": "ns",
            "range": "± 6060954.226653626"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 10000)",
            "value": 1299763793.3333333,
            "unit": "ns",
            "range": "± 14283690.75567202"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 10)",
            "value": 596685.720703125,
            "unit": "ns",
            "range": "± 38066.23108097078"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 100)",
            "value": 2496689.6627604165,
            "unit": "ns",
            "range": "± 6928.8674919508585"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 500)",
            "value": 10720389.911458334,
            "unit": "ns",
            "range": "± 15331.895723018311"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.OneProperty",
            "value": 11.939256797234217,
            "unit": "ns",
            "range": "± 0.1530720288419995"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.FiveProperties",
            "value": 39.24753963947296,
            "unit": "ns",
            "range": "± 0.23655916484513453"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.TwentyProperties",
            "value": 199.4675660530726,
            "unit": "ns",
            "range": "± 3.853149375209651"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 1000)",
            "value": 237985.1922200521,
            "unit": "ns",
            "range": "± 44.20131747165186"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 1000)",
            "value": 277779.77587890625,
            "unit": "ns",
            "range": "± 818.0821610563258"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 100000)",
            "value": 24769436.822916668,
            "unit": "ns",
            "range": "± 55095.741237141476"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 100000)",
            "value": 28009252.25,
            "unit": "ns",
            "range": "± 362037.0254742587"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 100)",
            "value": 112140.74271647136,
            "unit": "ns",
            "range": "± 2261.4375761582264"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 1000)",
            "value": 1082997.728515625,
            "unit": "ns",
            "range": "± 20042.889358574936"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 10000)",
            "value": 12304340.09375,
            "unit": "ns",
            "range": "± 189439.1521949052"
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
        "date": 1786341750284,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 1)",
            "value": 35046.1796875,
            "unit": "ns",
            "range": "± 6514.097419656515"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 10)",
            "value": 64608.469401041664,
            "unit": "ns",
            "range": "± 8197.350900955305"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.BindingReferencePlanBenchmarks.PrepareSharedHelperGraph(EntrypointCount: 100)",
            "value": 518784.6666666667,
            "unit": "ns",
            "range": "± 62124.602970392145"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 100)",
            "value": 3467.3234214782715,
            "unit": "ns",
            "range": "± 2.2700289618576908"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 100)",
            "value": 2842.5,
            "unit": "ns",
            "range": "± 324.2653234621303"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 1000)",
            "value": 32281.75341796875,
            "unit": "ns",
            "range": "± 25.326486777228233"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 1000)",
            "value": 2846.6666666666665,
            "unit": "ns",
            "range": "± 319.5314277709367"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.ValidateMapShape(EntryCount: 10000)",
            "value": 576497.6181640625,
            "unit": "ns",
            "range": "± 6375.663126918569"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Core.MapTraversalBenchmarks.MeterMapShape(EntryCount: 10000)",
            "value": 3142,
            "unit": "ns",
            "range": "± 194.56875391490794"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1)",
            "value": 49701.666666666664,
            "unit": "ns",
            "range": "± 2522.6512508337996"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 10)",
            "value": 141163,
            "unit": "ns",
            "range": "± 36539.33481879494"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 0, RequestCount: 1000)",
            "value": 7657943,
            "unit": "ns",
            "range": "± 55482.93403380899"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1)",
            "value": 50623.333333333336,
            "unit": "ns",
            "range": "± 2388.975582406261"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 10)",
            "value": 128967.66666666667,
            "unit": "ns",
            "range": "± 11918.908940558835"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 32, RequestCount: 1000)",
            "value": 7960856,
            "unit": "ns",
            "range": "± 55146.39338161654"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1)",
            "value": 51905.333333333336,
            "unit": "ns",
            "range": "± 755.5801303193019"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 10)",
            "value": 128099.5,
            "unit": "ns",
            "range": "± 5055.543887654423"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 1024, RequestCount: 1000)",
            "value": 8381202.666666667,
            "unit": "ns",
            "range": "± 64094.56579409313"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1)",
            "value": 73311,
            "unit": "ns",
            "range": "± 1603.2769567357975"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 10)",
            "value": 856031.3333333334,
            "unit": "ns",
            "range": "± 59189.30559090327"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Http.HttpGrantParsingBenchmarks.RepeatedHttpGets(ResponseBytes: 65536, RequestCount: 1000)",
            "value": 49725737.666666664,
            "unit": "ns",
            "range": "± 397361.5819896701"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 100)",
            "value": 19883.756612141926,
            "unit": "ns",
            "range": "± 81.90278347283648"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterExpressionBenchmarks.ExecuteArithmeticLoopAsync(Iterations: 10000)",
            "value": 58710.88928222656,
            "unit": "ns",
            "range": "± 172.15628117707814"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 12518.225036621094,
            "unit": "ns",
            "range": "± 3600.044176345931"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 13262.905110677084,
            "unit": "ns",
            "range": "± 2306.555542996004"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 11476.191345214844,
            "unit": "ns",
            "range": "± 997.783850730211"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.InMemoryRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 10877.348754882812,
            "unit": "ns",
            "range": "± 1436.4744705621354"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: False)",
            "value": 51019.233418782555,
            "unit": "ns",
            "range": "± 1632.3891636580202"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: False)",
            "value": 49851.263610839844,
            "unit": "ns",
            "range": "± 557.3578495848285"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.IntRoundTripAsync(LowAllocationProfile: True)",
            "value": 47578.955729166664,
            "unit": "ns",
            "range": "± 77.91724316797861"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.IpcRoundTripBenchmarks.StructPayloadRoundTripAsync(LowAllocationProfile: True)",
            "value": 45436.471842447914,
            "unit": "ns",
            "range": "± 1510.5647836439607"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.SerializeStructPayload",
            "value": 41.837194422880806,
            "unit": "ns",
            "range": "± 0.019243849641062554"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.MessagePackPayloadBenchmarks.DeserializeStructPayload",
            "value": 59.14456375439962,
            "unit": "ns",
            "range": "± 0.021041747890818724"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Int32)",
            "value": 10.698118701577187,
            "unit": "ns",
            "range": "± 0.02220318240148804"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Int32)",
            "value": 42.88746029138565,
            "unit": "ns",
            "range": "± 0.08141249291853399"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Int32)",
            "value": 28.071011672417324,
            "unit": "ns",
            "range": "± 0.049777084501127986"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Int32)",
            "value": 53.13508771856626,
            "unit": "ns",
            "range": "± 0.04932920837306955"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: String)",
            "value": 23.906221787134807,
            "unit": "ns",
            "range": "± 0.020477358156904504"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: String)",
            "value": 120.14538470904033,
            "unit": "ns",
            "range": "± 0.3988352488853663"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: String)",
            "value": 46.93514555692673,
            "unit": "ns",
            "range": "± 0.19038255977813034"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: String)",
            "value": 116.56403652826945,
            "unit": "ns",
            "range": "± 0.30663005935861537"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Enum)",
            "value": 10.710637867450714,
            "unit": "ns",
            "range": "± 0.009425176881710068"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Enum)",
            "value": 42.825454930464424,
            "unit": "ns",
            "range": "± 0.044473919791791855"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Enum)",
            "value": 28.09821531176567,
            "unit": "ns",
            "range": "± 0.12172862382830109"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Enum)",
            "value": 53.12382508317629,
            "unit": "ns",
            "range": "± 0.042514714518694284"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: ListInt32)",
            "value": 65.41519584258397,
            "unit": "ns",
            "range": "± 0.045898133838968576"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: ListInt32)",
            "value": 347.16099150975543,
            "unit": "ns",
            "range": "± 0.302355605953521"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: ListInt32)",
            "value": 53.59906395276388,
            "unit": "ns",
            "range": "± 0.058165844978643994"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: ListInt32)",
            "value": 393.2525358200073,
            "unit": "ns",
            "range": "± 0.4891417052668889"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: Dto)",
            "value": 46.676737209161125,
            "unit": "ns",
            "range": "± 0.047872636901204885"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: Dto)",
            "value": 255.12560558319092,
            "unit": "ns",
            "range": "± 1.9045001396076007"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: Dto)",
            "value": 56.368688782056175,
            "unit": "ns",
            "range": "± 0.34378969792655456"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: Dto)",
            "value": 297.2316756248474,
            "unit": "ns",
            "range": "± 0.9329090259070694"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: AnonymousDto)",
            "value": 46.794589598973594,
            "unit": "ns",
            "range": "± 0.02609377037602971"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: AnonymousDto)",
            "value": 385.14708216985065,
            "unit": "ns",
            "range": "± 0.5560232978376173"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: AnonymousDto)",
            "value": 74.97659516334534,
            "unit": "ns",
            "range": "± 0.1444020551274277"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: AnonymousDto)",
            "value": 430.0371265411377,
            "unit": "ns",
            "range": "± 1.2745877125112124"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.Encode(Projection: WholeEvent)",
            "value": 51.472707629203796,
            "unit": "ns",
            "range": "± 0.004187068630369115"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvoke(Projection: WholeEvent)",
            "value": 328.3631354967753,
            "unit": "ns",
            "range": "± 0.72948086873372"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.DecodeInvokeGenerated(Projection: WholeEvent)",
            "value": 52.14991839726766,
            "unit": "ns",
            "range": "± 0.2709223989939909"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushBenchmarks.RoundTrip(Projection: WholeEvent)",
            "value": 365.79856967926025,
            "unit": "ns",
            "range": "± 0.44895974524825744"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: False)",
            "value": 287823.5729166667,
            "unit": "ns",
            "range": "± 25405.8544645618"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 100, DuplicateLiterals: True)",
            "value": 1142770.7669270833,
            "unit": "ns",
            "range": "± 6098.300250341473"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: False)",
            "value": 3063040.6953125,
            "unit": "ns",
            "range": "± 88478.21810804872"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 1000, DuplicateLiterals: True)",
            "value": 91945039.27777778,
            "unit": "ns",
            "range": "± 1482584.3141761739"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: False)",
            "value": 15137264.401041666,
            "unit": "ns",
            "range": "± 815720.7316861072"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Json.JsonImportBenchmarks.Import(StatementCount: 5000, DuplicateLiterals: True)",
            "value": 2189179263.6666665,
            "unit": "ns",
            "range": "± 14846536.959789792"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 100)",
            "value": 6139395.380208333,
            "unit": "ns",
            "range": "± 1034752.7818420961"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 1000)",
            "value": 87897694.58333333,
            "unit": "ns",
            "range": "± 4070920.484909972"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginAnalyzerHelperGraphBenchmarks.AnalyzeHelperChain(HelperCount: 10000)",
            "value": 1342074596,
            "unit": "ns",
            "range": "± 19498124.540214732"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 10)",
            "value": 582730.0794270834,
            "unit": "ns",
            "range": "± 16708.58276353383"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 100)",
            "value": 2574446.259765625,
            "unit": "ns",
            "range": "± 6868.470584245173"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.PluginAnalyzer.PluginPackageGeneratorScaleBenchmarks.RunGenerators(KernelCount: 500)",
            "value": 11259597.286458334,
            "unit": "ns",
            "range": "± 36180.44954191102"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.OneProperty",
            "value": 13.516664038101831,
            "unit": "ns",
            "range": "± 0.1544165894471531"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.FiveProperties",
            "value": 38.48727113008499,
            "unit": "ns",
            "range": "± 0.41158977179444234"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.ConventionEventAdapterBenchmarks.TwentyProperties",
            "value": 144.59046280384064,
            "unit": "ns",
            "range": "± 1.5935798579978624"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 1000)",
            "value": 236118.51822916666,
            "unit": "ns",
            "range": "± 354.55993767414924"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 1000)",
            "value": 274121.86083984375,
            "unit": "ns",
            "range": "± 364.52910309284846"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.GetSettings(Iterations: 100000)",
            "value": 23664701.520833332,
            "unit": "ns",
            "range": "± 11810.872305198314"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Plugins.LiveSettingProxyBenchmarks.SetSettings(Iterations: 100000)",
            "value": 26431609.4375,
            "unit": "ns",
            "range": "± 12494.564316688566"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 100)",
            "value": 109968.98803710938,
            "unit": "ns",
            "range": "± 116.9712100477973"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 1000)",
            "value": 1112378.8567708333,
            "unit": "ns",
            "range": "± 2721.571086488019"
          },
          {
            "name": "DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierCallBenchmarks.VerifyRepeatedRuntimeCalls(CallCount: 10000)",
            "value": 11763808.75,
            "unit": "ns",
            "range": "± 128102.48503213815"
          }
        ]
      }
    ]
  }
}