using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.UI.Cancellation;

public sealed class UiLocalKernelCancellationTests
{
    [Fact]
    public async Task Caller_cancellation_from_local_kernel_preserves_the_caller_token_without_committing()
    {
        using var caller = new CancellationTokenSource();
        using var sandbox = SandboxHost.Create(builder => builder
            .AddDefaultPureBindings()
            .AddBinding(CancelCallerBinding(caller)));
        var package = UiTestFixture.Counter();
        package = package with
        {
            Kernels =
            [
                package.Kernels[0] with { ModuleJson = CancelingKernel() },
                package.Kernels[1]
            ]
        };
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox)
            .InstallAsync(package, renderer, TestUiTransport.Echo());

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.DispatchAsync(1, caller.Token).AsTask());

        Assert.Equal(caller.Token, exception.CancellationToken);
        var snapshot = await session.SnapshotAsync();
        Assert.Equal(0, snapshot.Version);
        Assert.Equal(0, UiTestFixture.Slot(snapshot, 1).Integer);
        Assert.Empty(renderer.Updates);
        Assert.False(session.IsDisconnected);
    }

    private static BindingDescriptor CancelCallerBinding(CancellationTokenSource caller)
        => new(
            "test.ui.cancelCaller",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                caller.Cancel();
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string CancelingKernel() => """
        {"id":"ui-cancel-caller","version":"1.0.0","targetSandboxVersion":"1.0.0",
        "capabilityRequests":[],"functions":[{"id":"main","visibility":"entrypoint",
        "parameters":[{"name":"value","type":"I32"}],"returnType":"I32",
        "body":[{"op":"expr","value":{"call":"test.ui.cancelCaller","args":[]}},
        {"op":"return","value":{"op":"add","left":{"var":"value"},"right":{"i32":1}}}]}]}
        """;
}
