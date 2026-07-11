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
        var scenario = CreateScenario(cancellation.Token);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                scenario.Context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                scenario.Dns,
                cancellation.Token));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        AssertNoPreCancellationSideEffects(scenario);
    }

    [Fact]
    public async Task GetTextAsync_with_operation_token_cancelled_after_dns_reports_cancelled()
    {
        using var operationCancellation = new CancellationTokenSource();
        var scenario = CreateScenario(
            CancellationToken.None,
            onDnsResolved: operationCancellation.Cancel);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                scenario.Context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                scenario.Dns,
                operationCancellation.Token));

        Assert.Equal(1, scenario.DnsCalls);
        var auditEvent = Assert.Single(scenario.Audit.Events, e => e.BindingId == "net.http.get" && !e.Success);
        var failures = new List<string>();
        if (ex.Error.Code != SandboxErrorCode.Cancelled)
        {
            failures.Add($"expected exception code Cancelled, observed {ex.Error.Code}");
        }

        if (auditEvent.ErrorCode != SandboxErrorCode.Cancelled)
        {
            failures.Add($"expected audit error Cancelled, observed {auditEvent.ErrorCode}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task GetTextAsync_with_context_token_cancelled_after_dns_reports_cancelled()
    {
        using var contextCancellation = new CancellationTokenSource();
        var scenario = CreateScenario(
            contextCancellation.Token,
            onDnsResolved: contextCancellation.Cancel);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                scenario.Context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                scenario.Dns,
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.Cancelled, ex.Error.Code);
        Assert.Equal(1, scenario.DnsCalls);
        var auditEvent = Assert.Single(scenario.Audit.Events, e => e.BindingId == "net.http.get" && !e.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, auditEvent.ErrorCode);
    }

    [Theory]
    [InlineData(false, "127.0.0.1")]
    [InlineData(true, "10.0.0.1")]
    public async Task GetTextAsync_with_token_cancelled_after_dns_private_address_reports_cancelled(
        bool cancelContext,
        string privateAddress)
    {
        using var operationCancellation = new CancellationTokenSource();
        using var contextCancellation = new CancellationTokenSource();
        var scenario = CreateScenario(
            contextCancellation.Token,
            onDnsResolved: cancelContext ? contextCancellation.Cancel : operationCancellation.Cancel,
            IPAddress.Parse(privateAddress));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                scenario.Context,
                new SandboxUri("https://api.example.com/config"),
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                scenario.Dns,
                operationCancellation.Token));

        Assert.Equal(1, scenario.DnsCalls);
        var auditEvent = Assert.Single(scenario.Audit.Events, e => e.BindingId == "net.http.get" && !e.Success);
        var failures = new List<string>();
        if (ex.Error.Code != SandboxErrorCode.Cancelled)
        {
            failures.Add($"expected exception code Cancelled, observed {ex.Error.Code}");
        }

        if (auditEvent.ErrorCode != SandboxErrorCode.Cancelled)
        {
            failures.Add($"expected audit error Cancelled, observed {auditEvent.ErrorCode}");
        }

        Assert.Empty(failures);
    }

    private static SafeHttpCancellationScenario CreateScenario(
        CancellationToken contextToken,
        Action? onDnsResolved = null,
        params IPAddress[] addresses)
    {
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
            contextToken);

        var scenario = new SafeHttpCancellationScenario(context, audit);
        scenario.Dns = (_, _) =>
        {
            scenario.DnsCalls++;
            onDnsResolved?.Invoke();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(
                addresses.Length == 0 ? [IPAddress.Parse("93.184.216.34")] : addresses);
        };

        return scenario;
    }

    private static void AssertNoPreCancellationSideEffects(SafeHttpCancellationScenario scenario)
    {
        var failures = new List<string>();
        if (scenario.DnsCalls != 0)
        {
            failures.Add($"expected no DNS calls, observed {scenario.DnsCalls}");
        }

        if (scenario.Context.Budget.NetworkBytesWritten != 0)
        {
            failures.Add($"expected no network bytes written, observed {scenario.Context.Budget.NetworkBytesWritten}");
        }

        if (scenario.Context.Budget.NetworkBytesRead != 0)
        {
            failures.Add($"expected no network bytes read, observed {scenario.Context.Budget.NetworkBytesRead}");
        }

        var httpAuditCount = scenario.Audit.Events.Count(e => e.BindingId == "net.http.get");
        if (httpAuditCount != 0)
        {
            failures.Add($"expected no net.http.get audit events, observed {httpAuditCount}");
        }

        Assert.Empty(failures);
    }

    private sealed class SafeHttpCancellationScenario(
        SandboxContext context,
        InMemoryAuditSink audit)
    {
        public SandboxContext Context { get; } = context;

        public InMemoryAuditSink Audit { get; } = audit;

        public SafeDnsResolver Dns { get; set; } = (_, _) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([]);

        public int DnsCalls { get; set; }
    }
}
