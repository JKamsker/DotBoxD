using BenchmarkDotNet.Attributes;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

[MemoryDiagnoser]
public class DetachedDebugCheckpointBenchmarks
{
    private Hosting.Execution.SandboxHost _host = null!;
    private ExecutionPlan _plan = null!;

    [Params(1, 1024)]
    public int StatementCount { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = Hosting.Execution.SandboxHost.Create(builder => builder.UseInterpreter());
        _plan = await _host.PrepareAsync(
            Module(StatementCount),
            SandboxPolicyBuilder.Create().WithFuel(StatementCount * 10L + 100).Build());
    }

    [GlobalCleanup]
    public void Cleanup() => _host.Dispose();

    [Benchmark]
    public ValueTask<SandboxExecutionResult> ExecuteDetachedAsync()
        => _host.ExecuteAsync(
            _plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

    private static SandboxModule Module(int statementCount)
    {
        var span = new SourceSpan(0, 0);
        var statements = Enumerable.Range(0, statementCount)
            .Select(_ => (Statement)new ExpressionStatement(new LiteralExpression(SandboxValue.Unit, span), span))
            .Append(new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span))
            .ToArray();
        return new SandboxModule(
            "detached-debug-benchmark-" + statementCount,
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [new SandboxFunction("main", true, [], SandboxType.Unit, statements)],
            new Dictionary<string, string>());
    }
}
