using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsCancellationTests
{
    [Fact]
    public async Task GetTextAsync_cancels_ignoring_dns_resolver_without_late_audit_or_accounting()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new PendingDnsResolver();
        var scenario = CreateScenario();
        var context = scenario.Context;
        var request = SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("unexpected transport call"),
                resolver.ResolveAsync,
                cancellation.Token)
            .AsTask();

        await resolver.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        try
        {
            var exception = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
                await request.WaitAsync(TimeSpan.FromMilliseconds(250)));

            Assert.Equal(SandboxErrorCode.Cancelled, exception.Error.Code);
            var auditCount = scenario.Audit.Events.Count;
            var bytesRead = context.Budget.NetworkBytesRead;
            var bytesWritten = context.Budget.NetworkBytesWritten;

            resolver.Release();
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.Equal(auditCount, scenario.Audit.Events.Count);
            Assert.Equal(bytesRead, context.Budget.NetworkBytesRead);
            Assert.Equal(bytesWritten, context.Budget.NetworkBytesWritten);
        }
        finally
        {
            resolver.Release();
            await ObserveCompletionAsync(request);
        }
    }

    private static (SandboxContext Context, InMemoryAuditSink Audit) CreateScenario()
    {
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
        return (context, audit);
    }

    private static async Task ObserveCompletionAsync(Task request)
    {
        try
        {
            await request.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (SandboxRuntimeException)
        {
        }
    }

    private sealed class PendingDnsResolver
    {
        private readonly TaskCompletionSource<IReadOnlyList<IPAddress>> _addresses = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string _, CancellationToken __)
        {
            Started.TrySetResult();
            return new ValueTask<IReadOnlyList<IPAddress>>(_addresses.Task);
        }

        public void Release()
            => _addresses.TrySetResult([IPAddress.Parse("93.184.216.34")]);
    }
}
