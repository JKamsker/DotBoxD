window.BENCHMARK_DATA = {
  "lastUpdate": 1783929460371,
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
      }
    ]
  }
}