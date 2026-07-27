using BenchmarkDotNet.Attributes;
using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class RpcTelemetryBenchmarks
{
    [Benchmark]
    public void SuccessfulRequestWithoutListeners()
    {
        using var scope = RpcTelemetry.StartServerRequest();
        scope.SetResolvedOperation("GameService", "MovePlayerAsync");
    }
}
