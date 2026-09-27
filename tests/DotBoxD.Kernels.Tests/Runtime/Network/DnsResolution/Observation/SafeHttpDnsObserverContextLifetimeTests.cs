using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsObserverContextLifetimeTests
{
    [Theory]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("context", false)]
    [InlineData("context", true)]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    public async Task Canceled_request_releases_ambient_state_while_resolver_remains_pending(
        string mode, bool customSource)
    {
        var policy = SandboxPolicyBuilder.Create().GrantHttpGet(["api.example.com"], maxResponseBytes: 1024,
                timeout: TimeSpan.FromSeconds(mode == "timeout" ? 1 : 10))
            .WithFuel(5_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
        using var caller = new CancellationTokenSource();
        using var contextCancellation = new CancellationTokenSource();
        using var context = new SandboxContext(SandboxRunId.New(), policy, new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(), new InMemoryAuditSink(), contextCancellation.Token);
        using var invoker = new SafeInMemoryHttpMessageInvoker("ok");
        var resolution = new PendingDnsResolution(customSource);
        Task<string> request = null!;
        var reference = AmbientContextLifetime.Capture(() =>
            request = SafeHttpClient.GetTextAsync(context, new SandboxUri("https://api.example.com/config"),
                invoker, resolution.Resolve, caller.Token).AsTask());
        try
        {
            AmbientContextLifetime.AssertRetained(reference);
            if (mode == "caller")
            {
                caller.Cancel();
            }
            else if (mode == "context")
            {
                contextCancellation.Cancel();
            }
            var error = await Assert.ThrowsAsync<SandboxRuntimeException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(mode == "timeout" ? SandboxErrorCode.Timeout : SandboxErrorCode.Cancelled, error.Error.Code);
            Assert.False(resolution.IsCompleted);

            await AmbientContextLifetime.AssertCollectedAsync(reference);
            Assert.False(resolution.IsCompleted);
            GC.KeepAlive(request);
        }
        finally
        {
            resolution.Succeed();
        }
    }
}
