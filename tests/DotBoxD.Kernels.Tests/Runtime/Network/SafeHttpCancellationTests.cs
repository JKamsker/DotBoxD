using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpCancellationTests
{
    [Fact]
    public async Task GetTextAsync_with_pre_canceled_token_does_not_resolve_or_account_network()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var audit = new InMemoryAuditSink();
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            audit,
            cancellation.Token);
        var dnsCalls = 0;
        SafeDnsResolver dns = (_, _) =>
        {
            dnsCalls++;
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        };

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                dns,
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationSideEffects(context, audit, dnsCalls);
    }

    private static void AssertNoPreCancellationSideEffects(
        SandboxContext context,
        InMemoryAuditSink audit,
        int dnsCalls)
    {
        var failures = new List<string>();
        if (dnsCalls != 0)
        {
            failures.Add($"expected no DNS calls, observed {dnsCalls}");
        }

        if (context.Budget.NetworkBytesWritten != 0)
        {
            failures.Add($"expected no network bytes written, observed {context.Budget.NetworkBytesWritten}");
        }

        if (context.Budget.NetworkBytesRead != 0)
        {
            failures.Add($"expected no network bytes read, observed {context.Budget.NetworkBytesRead}");
        }

        var httpAuditCount = audit.Events.Count(e => e.BindingId == "net.http.get");
        if (httpAuditCount != 0)
        {
            failures.Add($"expected no net.http.get audit events, observed {httpAuditCount}");
        }

        Assert.Empty(failures);
    }
}
