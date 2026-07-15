using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class InterpreterDebugHookTests
{
    [Fact]
    public async Task Attached_hook_forces_compiled_execution_to_the_interpreter()
    {
        var hook = new RecordingHook();
        var (host, plan) = await PrepareAsync(LoopAndCallModule(), compiler: true);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                AllowFallbackToInterpreter = false,
                DebugHook = hook
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(2, Assert.IsType<I32Value>(result.Value).Value);
        Assert.NotEmpty(hook.Checkpoints);
    }

    [Fact]
    public async Task Checkpoints_cover_nodes_loops_calls_and_logical_frames()
    {
        var hook = new RecordingHook();
        var (host, plan) = await PrepareAsync(LoopAndCallModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(hook.Checkpoints, checkpoint => checkpoint.Kind == SandboxDebugCheckpointKind.Statement);
        Assert.Contains(hook.Checkpoints, checkpoint => checkpoint.Kind == SandboxDebugCheckpointKind.Expression);
        Assert.Contains(hook.Checkpoints, checkpoint => checkpoint.Kind == SandboxDebugCheckpointKind.Call);
        Assert.Equal(2, hook.Checkpoints.Count(checkpoint => checkpoint.Kind == SandboxDebugCheckpointKind.LoopIteration));

        var helperEntry = Assert.Single(hook.Checkpoints, checkpoint =>
            checkpoint.Kind == SandboxDebugCheckpointKind.FunctionEntry &&
            checkpoint.Frame.FunctionId == "addOne" &&
            checkpoint.Frame.Arguments is [{ Value: I32Value { Value: 0 } }]);
        Assert.Equal(1, helperEntry.Frame.Depth);
        Assert.Equal("main", helperEntry.Frame.Caller!.FunctionId);

        var mainExit = Assert.Single(hook.Checkpoints, checkpoint =>
            checkpoint.Kind == SandboxDebugCheckpointKind.FunctionExit &&
            checkpoint.Frame.FunctionId == "main");
        Assert.Equal(2, Assert.IsType<I32Value>(mainExit.Value).Value);
        Assert.All(hook.Checkpoints, checkpoint => Assert.Equal(SandboxNodeId.CurrentVersion, checkpoint.Node.Id.Version));
    }

    [Fact]
    public async Task Sandbox_exceptions_stop_on_the_faulting_frame()
    {
        var hook = new RecordingHook();
        var (host, plan) = await PrepareAsync(DivideByZeroModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.False(result.Succeeded);
        var stopped = Assert.Single(
            hook.Checkpoints,
            checkpoint => checkpoint.Kind == SandboxDebugCheckpointKind.Exception);
        Assert.Equal("main", stopped.Frame.FunctionId);
        Assert.Equal(result.Error, stopped.Error);
        Assert.Equal(SandboxNodeKind.Expression, stopped.Node.Kind);
        Assert.Equal("body/0/value", stopped.Node.StructuralPath);
    }

    [Fact]
    public async Task Time_stopped_in_hook_does_not_consume_wall_time()
    {
        var hook = new DelayingHook(TimeSpan.FromMilliseconds(60));
        var (host, plan) = await PrepareAsync(
            ConstantModule(),
            wallTime: TimeSpan.FromMilliseconds(20));

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { DebugHook = hook });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(hook.CheckpointCount >= 3);
    }

    [Fact]
    public async Task Worker_process_debugging_is_rejected_before_dispatch()
    {
        var hook = new RecordingHook();
        var (host, plan) = await PrepareAsync(ConstantModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Isolation = SandboxIsolation.WorkerProcess,
                DebugHook = hook
            });

        Assert.False(result.Succeeded);
        Assert.False(result.ExecutionDispatched);
        Assert.Contains("worker-process interpreter debugging", result.Error!.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(hook.Checkpoints);
    }

    [Fact]
    public async Task Hook_failure_detaches_and_execution_resumes()
    {
        var (host, plan) = await PrepareAsync(ConstantModule());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { DebugHook = new ThrowingHook() });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
    }

    private static async Task<(DotBoxD.Hosting.Execution.SandboxHost Host, ExecutionPlan Plan)> PrepareAsync(
        SandboxModule module,
        bool compiler = false,
        TimeSpan? wallTime = null)
    {
        var host = SandboxTestHost.Create(compiler);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithWallTime(wallTime ?? TimeSpan.FromSeconds(2))
            .Build();
        return (host, await host.PrepareAsync(module, policy));
    }

    private static SandboxModule LoopAndCallModule()
    {
        var span = new SourceSpan(1, 1);
        var main = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement("total", Literal(0), span),
                new ForRangeStatement(
                    "i",
                    Literal(0),
                    Literal(2),
                    [new AssignmentStatement("total", new CallExpression("addOne", [Variable("total")], null, span), span)],
                    span),
                new ReturnStatement(Variable("total"), span)
            ]);
        var helper = new SandboxFunction(
            "addOne",
            false,
            [new Parameter("value", SandboxType.I32)],
            SandboxType.I32,
            [new ReturnStatement(new BinaryExpression(Variable("value"), "+", Literal(1), span), span)]);
        return Module([main, helper]);

        LiteralExpression Literal(int value) => new(SandboxValue.FromInt32(value), span);
        VariableExpression Variable(string name) => new(name, span);
    }

    private static SandboxModule DivideByZeroModule()
    {
        var span = new SourceSpan(1, 1);
        return Module([new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new BinaryExpression(Literal(1), "/", Literal(0), span), span)])]);

        LiteralExpression Literal(int value) => new(SandboxValue.FromInt32(value), span);
    }

    private static SandboxModule ConstantModule()
    {
        var span = new SourceSpan(1, 1);
        return Module([new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(7), span), span)])]);
    }

    private static SandboxModule Module(IReadOnlyList<SandboxFunction> functions)
        => new("debug-runtime", new SemVersion(1, 0, 0), new SemVersion(1, 0, 0), [], functions, new Dictionary<string, string>());

    private sealed class RecordingHook : ISandboxExecutionDebugHook
    {
        public List<SandboxDebugCheckpoint> Checkpoints { get; } = [];

        public ValueTask OnCheckpointAsync(SandboxDebugCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            Checkpoints.Add(checkpoint);
            return default;
        }
    }

    private sealed class DelayingHook(TimeSpan delay) : ISandboxExecutionDebugHook
    {
        public int CheckpointCount { get; private set; }

        public async ValueTask OnCheckpointAsync(SandboxDebugCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            CheckpointCount++;
            await Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class ThrowingHook : ISandboxExecutionDebugHook
    {
        public ValueTask OnCheckpointAsync(SandboxDebugCheckpoint checkpoint, CancellationToken cancellationToken)
            => ValueTask.FromException(new IOException("debug bridge failed"));
    }
}
