using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Debugging.Clr;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class TrustedClrPluginDebugEvaluatorTests
{
    [Fact]
    public async Task In_process_provider_supports_live_context_full_csharp_and_await()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedInProcess(
            new TrustedInProcessDebugEvaluatorOptions
            {
                Context = new Dictionary<string, object?> { ["offset"] = 2 }
            });
        var frame = new TestFrame(SandboxValue.FromInt32(40));

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                frame,
                "await Task.FromResult(amount + (int)Context[\"offset\"]!)",
                allowAwait: true));

        Assert.Equal(PluginDebugEvaluationTrustProfile.TrustedInProcess, evaluator.TrustProfile);
        Assert.True(evaluator.SupportsAwait);
        Assert.Equal(SandboxValue.FromInt32(42), result.Value);
    }

    [Fact]
    public async Task In_process_provider_rejects_results_that_cannot_cross_sandbox_boundary()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedInProcess(
            new TrustedInProcessDebugEvaluatorOptions());

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(new TestFrame(SandboxValue.FromInt32(1)), "new object()"));

        Assert.False(result.Succeeded);
        Assert.Contains("cannot cross", result.Error!.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_process_provider_can_read_map_frame_values()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedInProcess(
            new TrustedInProcessDebugEvaluatorOptions());
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("first")] = SandboxValue.FromInt32(1),
                [SandboxValue.FromString("second")] = SandboxValue.FromInt32(2)
            },
            SandboxType.String,
            SandboxType.I32);

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(new TestFrame(map), "amount.Count"));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromInt32(2), result.Value);
    }

    [Fact]
    public async Task In_process_provider_can_return_empty_sandbox_maps()
    {
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>(),
            SandboxType.String,
            SandboxType.I32);
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedInProcess(
            new TrustedInProcessDebugEvaluatorOptions
            {
                Context = new Dictionary<string, object?> { ["map"] = map }
            });

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                new TestFrame(SandboxValue.FromInt32(1)),
                "(DotBoxD.Kernels.Sandbox.SandboxValue)Context[\"map\"]!"));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(map, result.Value);
    }

    [Fact]
    public async Task Trusted_provider_compiles_against_lazily_supplied_assembly_image()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedInProcess(
            new TrustedInProcessDebugEvaluatorOptions());
        var image = await File.ReadAllBytesAsync(typeof(TrustedClrPluginDebugEvaluatorTests).Assembly.Location);
        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                new TestFrame(SandboxValue.FromInt32(1)),
                "typeof(DotBoxD.Kernels.Tests.Plugins.Debugging.Execution.TrustedClrPluginDebugEvaluatorTests).Name",
                assemblies: new Dictionary<string, ReadOnlyMemory<byte>>
                {
                    ["DotBoxD.Kernels.Tests.dll"] = image
                }));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromString(nameof(TrustedClrPluginDebugEvaluatorTests)), result.Value);
    }

    [Fact]
    public async Task Worker_provider_evaluates_serialized_frame_in_disposable_process()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedWorker(
            new TrustedWorkerDebugEvaluatorOptions
            {
                TimeLimit = TimeSpan.FromSeconds(20),
                MemoryLimitBytes = 768L * 1024 * 1024,
                Context = new Dictionary<string, SandboxValue>
                {
                    ["offset"] = SandboxValue.FromInt32(2)
                }
            });

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                new TestFrame(SandboxValue.FromInt32(40)),
                "await Task.FromResult(amount + (int)Context[\"offset\"]!)",
                allowAwait: true));

        Assert.Equal(PluginDebugEvaluationTrustProfile.TrustedWorker, evaluator.TrustProfile);
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromInt32(42), result.Value);
    }

    [Fact]
    public async Task Worker_provider_enforces_time_limit_by_terminating_child_process()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedWorker(
            new TrustedWorkerDebugEvaluatorOptions
            {
                TimeLimit = TimeSpan.FromMilliseconds(100),
                MemoryLimitBytes = 768L * 1024 * 1024
            });

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                new TestFrame(SandboxValue.FromInt32(1)),
                "new Func<int>(() => { Thread.Sleep(5000); return amount; })()"));

        Assert.False(result.Succeeded);
        Assert.Contains("time limit", result.Error!.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_provider_enforces_memory_limit_by_terminating_child_process()
    {
        var evaluator = ClrPluginDebugEvaluators.CreateTrustedWorker(
            new TrustedWorkerDebugEvaluatorOptions
            {
                TimeLimit = TimeSpan.FromSeconds(20),
                MemoryLimitBytes = 128L * 1024 * 1024
            });

        var result = await evaluator.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                new TestFrame(SandboxValue.FromInt32(1)),
                "new Func<int>(() => { var bytes = new byte[256 * 1024 * 1024]; " +
                "System.Array.Fill(bytes, (byte)1); System.Threading.Thread.Sleep(5000); " +
                "return amount + bytes[0]; })()"));

        Assert.False(result.Succeeded);
        Assert.Contains("memory limit", result.Error!.SafeMessage, StringComparison.Ordinal);
    }

    private sealed class TestFrame(SandboxValue amount) : ISandboxDebugFrame
    {
        public string FunctionId => "test";

        public int Depth => 0;

        public ISandboxDebugFrame? Caller => null;

        public IReadOnlyList<SandboxDebugVariable> Arguments { get; } =
        [
            new SandboxDebugVariable(
                "amount",
                amount.Type,
                SandboxDebugVariableKind.Argument,
                isAssigned: true,
                amount)
        ];

        public IReadOnlyList<SandboxDebugVariable> Locals => Array.Empty<SandboxDebugVariable>();

        public bool TrySetVariable(string name, SandboxValue value, out SandboxError? error)
        {
            error = new SandboxError(SandboxErrorCode.InvalidInput, "read only");
            return false;
        }

        public bool TrySetMember(
            string name,
            IReadOnlyList<SandboxDebugValuePathSegment> path,
            SandboxValue value,
            out SandboxError? error)
        {
            error = new SandboxError(SandboxErrorCode.InvalidInput, "read only");
            return false;
        }
    }
}
