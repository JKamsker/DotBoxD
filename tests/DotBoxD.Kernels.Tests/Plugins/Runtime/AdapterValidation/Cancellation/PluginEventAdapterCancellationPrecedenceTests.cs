using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterCancellationPrecedenceTests
{
    [Fact]
    public Task HandleAsync_prioritizes_caller_cancellation_after_adapter_fault()
        => AssertCallerCancellationWinsAsync(
            (kernel, adapter, e, cancellationToken) =>
                kernel.HandleAsync(adapter, e, cancellationToken).AsTask());

    [Fact]
    public Task ShouldHandleAsync_prioritizes_caller_cancellation_after_adapter_fault()
        => AssertCallerCancellationWinsAsync(
            (kernel, adapter, e, cancellationToken) =>
                kernel.ShouldHandleAsync(adapter, e, cancellationToken).AsTask());

    [Fact]
    public async Task Ordinary_adapter_callback_faults_remain_validation_failures()
    {
        var kernel = await InstallKernelAsync();
        var adapter = new FaultingAdapter();
        var e = new AdapterEvent("player-1");

        var handleException = await Assert.ThrowsAsync<SandboxValidationException>(
            () => kernel.HandleAsync(adapter, e).AsTask());
        var shouldHandleException = await Assert.ThrowsAsync<SandboxValidationException>(
            () => kernel.ShouldHandleAsync(adapter, e).AsTask());

        Assert.Contains(handleException.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Contains(shouldHandleException.Diagnostics, diagnostic => diagnostic.Code == "DBXK036");
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Fact]
    public async Task Adapter_that_cancels_and_returns_observes_caller_cancellation()
    {
        var kernel = await InstallKernelAsync();
        using var cancellation = new CancellationTokenSource();
        var adapter = new CancellingAdapter(cancellation);

        var exception = await Record.ExceptionAsync(
            () => kernel.HandleAsync(adapter, new AdapterEvent("player-1"), cancellation.Token).AsTask());

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
    }

    private static async Task AssertCallerCancellationWinsAsync(
        Func<InstalledKernel, IPluginEventAdapter<AdapterEvent>, AdapterEvent, CancellationToken, Task> invoke)
    {
        var kernel = await InstallKernelAsync();
        using var cancellation = new CancellationTokenSource();
        var adapter = new CancelThenFaultAdapter(cancellation);

        var exception = await Record.ExceptionAsync(
            () => invoke(kernel, adapter, new AdapterEvent("player-1"), cancellation.Token));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(canceled.ToString().Contains(CancelThenFaultAdapter.FailureMessage, StringComparison.Ordinal));
        Assert.Empty(kernel.ExecutionObservations);
        Assert.Null(kernel.LastExecution);
    }

    private static async Task<InstalledKernel> InstallKernelAsync()
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Interpreted);
        return await server.InstallAsync(CreatePackage());
    }

    private static PluginPackage CreatePackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] { new("e_TargetId", SandboxType.String) };

        return PluginPackage.Create(
            new PluginManifest(
                "adapter-cancellation-precedence",
                "IEventKernel<AdapterEvent>",
                ExecutionMode.Interpreted,
                ["Cpu"],
                [],
                [new HookSubscriptionManifest(nameof(AdapterEvent), "AdapterCancellationKernel")]),
            new SandboxModule(
                "adapter-cancellation-precedence",
                SemVersion.One,
                SemVersion.One,
                [],
                [
                    new SandboxFunction(
                        "ShouldHandle",
                        true,
                        parameters,
                        SandboxType.Bool,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), span), span)]),
                    new SandboxFunction(
                        "Handle",
                        true,
                        parameters,
                        SandboxType.Unit,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span)])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "adapter-cancellation-precedence",
                    ["kernel"] = "AdapterCancellationKernel"
                }));
    }

    private sealed record AdapterEvent(string TargetId);

    private abstract class Adapter : IPluginEventAdapter<AdapterEvent>
    {
        public string EventName => nameof(AdapterEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public abstract IReadOnlyList<SandboxValue> ToSandboxValues(AdapterEvent e);
    }

    private sealed class CancelThenFaultAdapter(CancellationTokenSource cancellation) : Adapter
    {
        public const string FailureMessage = "adapter callback failure";

        public override IReadOnlyList<SandboxValue> ToSandboxValues(AdapterEvent e)
        {
            cancellation.Cancel();
            throw new InvalidOperationException(FailureMessage);
        }
    }

    private sealed class FaultingAdapter : Adapter
    {
        public override IReadOnlyList<SandboxValue> ToSandboxValues(AdapterEvent e)
            => throw new InvalidOperationException();
    }

    private sealed class CancellingAdapter(CancellationTokenSource cancellation) : Adapter
    {
        public override IReadOnlyList<SandboxValue> ToSandboxValues(AdapterEvent e)
        {
            cancellation.Cancel();
            return [SandboxValue.FromString(e.TargetId)];
        }
    }
}
