using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsFaultCancellationPrecedenceTests
{
    [Fact]
    public async Task GetTextAsync_gives_caller_cancellation_precedence_over_resolver_fault()
    {
        using var cancellation = new CancellationTokenSource();
        const string resolverFailure = "resolver callback failure";
        var resolverCalls = 0;
        SafeDnsResolver resolver = (_, _) =>
        {
            resolverCalls++;
            cancellation.Cancel();
            return ValueTask.FromException<IReadOnlyList<IPAddress>>(
                new InvalidOperationException(resolverFailure));
        };
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var audit = new InMemoryAuditSink();
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            audit,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("unexpected transport response"),
                resolver,
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, exception.Error.Code);
        Assert.DoesNotContain(resolverFailure, exception.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(0, context.Budget.NetworkBytesRead);
        var auditEvent = Assert.Single(audit.Events, e => e.BindingId == "net.http.get" && !e.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, auditEvent.ErrorCode);
    }
}
