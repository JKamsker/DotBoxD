using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionVoidProxyTests
{
    [Fact]
    public async Task Void_proxy_still_rejects_non_unit_results()
    {
        using var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = ServerExtensionVoidProxyFixture.Package(
            SandboxType.I32,
            new LiteralExpression(SandboxValue.FromInt32(42), ServerExtensionVoidProxyFixture.Span));
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IVoidServerExtension>(kernel);

        var error = Assert.Throws<NotSupportedException>(service.Invoke);

        Assert.Contains("Unit", error.Message, StringComparison.Ordinal);
        Assert.Contains("I32", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Void_proxy_waits_for_pending_completion_and_preserves_failures(bool fail)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string bindingId = "test.pending.unit";
        var binding = new BindingDescriptor(
            bindingId, SemVersion.One, [], SandboxType.Unit, SandboxEffect.Cpu, null,
            BindingCostModel.Fixed(1), AuditLevel.None, BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<SandboxValue>(completion.Task);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };
        var policy = SandboxPolicyBuilder.Create().AllowRuntimeAsync().WithWallTime(TimeSpan.FromSeconds(10)).Build();
        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBinding(binding),
            defaultPolicy: policy,
            executionMode: ExecutionMode.Interpreted);
        var package = ServerExtensionVoidProxyFixture.Package(
            SandboxType.Unit,
            new CallExpression(bindingId, [], null, ServerExtensionVoidProxyFixture.Span),
            isAsync: true);
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IVoidServerExtension>(kernel);
        var call = Task.Run(service.Invoke);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(call.IsCompleted);
            if (fail)
            {
                completion.SetException(new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.BindingFailure, "expected binding failure")));
                var error = await Assert.ThrowsAsync<SandboxRuntimeException>(
                    () => call.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(SandboxErrorCode.BindingFailure, error.Error.Code);
            }
            else
            {
                completion.SetResult(SandboxValue.Unit);
                await call.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(kernel.LastExecution!.Succeeded);
            }
        }
        finally
        {
            completion.TrySetResult(SandboxValue.Unit);
            await call.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }
}
