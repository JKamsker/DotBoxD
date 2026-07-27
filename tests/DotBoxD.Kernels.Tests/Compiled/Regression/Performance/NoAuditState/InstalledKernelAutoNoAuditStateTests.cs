using System.Reflection;
using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Regression.Performance.AttemptResult;
using DotBoxD.Kernels.Verifier.Generated;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class InstalledKernelAutoNoAuditStateTests
{
    private static readonly FieldInfo PreparedValueStateField = typeof(InstalledKernel).GetField(
        "_preparedValueState",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("InstalledKernel prepared-value state field was not found.");

    private static readonly NoAuditEvent Event = new();

    [Fact]
    public async Task Auto_state_is_seeded_only_after_compiled_success_and_reused_by_later_runs()
    {
        var compiler = new FailFirstCompiler();
        using var server = CreateServer(compiler);
        var kernel = await server.InstallAsync(Package());

        Assert.Null(PreparedValueState(kernel));

        Assert.False(await InvokeAsync(kernel));
        Assert.Equal(ExecutionMode.Interpreted, kernel.LastExecution!.ActualMode);
        Assert.Null(PreparedValueState(kernel));

        Assert.False(await InvokeAsync(kernel));
        Assert.Equal(ExecutionMode.Interpreted, kernel.LastExecution!.ActualMode);
        Assert.Equal(SandboxErrorCode.VerifierFailure, kernel.LastExecution.FallbackReason);
        Assert.Null(PreparedValueState(kernel));

        Assert.False(await InvokeAsync(kernel));
        Assert.Equal(ExecutionMode.Compiled, kernel.LastExecution!.ActualMode);
        var state = Assert.IsType<CompiledNoAuditRunState>(PreparedValueState(kernel));
        Assert.Equal(0, state.Budget.FuelUsed);

        Assert.False(await InvokeAsync(kernel));
        Assert.Same(state, PreparedValueState(kernel));
        var reusedUsage = state.Budget.Snapshot();
        Assert.True(reusedUsage.FuelUsed > 0);

        state.Budget.ChargeFuel(1);
        Assert.Equal(reusedUsage.FuelUsed + 1, state.Budget.FuelUsed);

        Assert.False(await InvokeAsync(kernel));
        Assert.Same(state, PreparedValueState(kernel));
        Assert.Equal(reusedUsage, state.Budget.Snapshot());
    }

    [Fact]
    public async Task Revocation_during_first_auto_compile_does_not_seed_state()
    {
        var compiler = new GatedSuccessCompiler();
        using var server = CreateServer(compiler);
        var kernel = await server.InstallAsync(Package());

        Assert.False(await InvokeAsync(kernel));
        Assert.Null(PreparedValueState(kernel));

        var pending = InvokeAsync(kernel).AsTask();
        await compiler.Gate.WaitUntilEnteredAsync();
        kernel.Revoke();
        compiler.Gate.Release();

        var error = await Assert.ThrowsAsync<SandboxRuntimeException>(async () => await pending);
        Assert.Equal(SandboxErrorCode.PolicyDenied, error.Error.Code);
        Assert.True(kernel.IsRevoked);
        Assert.Null(PreparedValueState(kernel));
        Assert.False(kernel.LastExecution!.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, kernel.LastExecution.ErrorCode);
    }

    private static PluginServer CreateServer(ISandboxCompiler compiler)
        => PluginServer.Create(
            messages: new InMemoryPluginMessageSink(),
            configureHost: builder =>
            {
                builder.UseCompilerIfAvailable(compiler);
                builder.UseExecutionModeSelector(new AlwaysCompiledSelector());
            },
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Auto);

    private static ValueTask<bool> InvokeAsync(InstalledKernel kernel)
        => kernel.ShouldHandleAsync(NoAuditEventAdapter.Instance, Event);

    private static CompiledNoAuditRunState? PreparedValueState(InstalledKernel kernel)
        => (CompiledNoAuditRunState?)PreparedValueStateField.GetValue(kernel);

    private static PluginPackage Package()
    {
        var span = new SourceSpan(1, 1);
        var shouldHandle = new SandboxFunction(
            "ShouldHandle",
            IsEntrypoint: true,
            [],
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(false), span), span)]);
        var handle = new SandboxFunction(
            "Handle",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span)]);
        var manifest = new PluginManifest(
            "auto-no-audit-state",
            "IEventKernel<NoAuditEvent>",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [new HookSubscriptionManifest(NoAuditEventAdapter.Name, "NoAuditKernel")]);
        var module = new SandboxModule(
            "auto-no-audit-state",
            SemVersion.One,
            SemVersion.One,
            [],
            [shouldHandle, handle],
            new Dictionary<string, string>
            {
                ["pluginId"] = "auto-no-audit-state",
                ["kernel"] = "NoAuditKernel"
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private sealed class AlwaysCompiledSelector : IExecutionModeSelector
    {
        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
            => ExecutionModeDecision.Compiled;
    }

    private sealed class FailFirstCompiler : ISandboxCompiler
    {
        private readonly ReflectionEmitSandboxCompiler _inner = new(new GeneratedAssemblyVerifier());
        private int _calls;

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            _calls++;
            return _calls == 1
                ? ValueTask.FromException<CompiledArtifact>(new SandboxRuntimeException(
                    new SandboxError(SandboxErrorCode.VerifierFailure, "first compilation rejected")))
                : _inner.CompileAsync(plan, options, cancellationToken);
        }
    }

    private sealed record NoAuditEvent;

    private sealed class NoAuditEventAdapter : IPluginEventAdapter<NoAuditEvent>
    {
        public const string Name = "NoAuditEvent";

        public static NoAuditEventAdapter Instance { get; } = new();

        public string EventName => Name;
        public IReadOnlyList<Parameter> Parameters { get; } = [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NoAuditEvent e) => [];
    }
}
