using System.Net;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsSnapshotContractTests
{
    [Theory]
    [InlineData("null", false)]
    [InlineData("null", true)]
    [InlineData("null-entry", false)]
    [InlineData("null-entry", true)]
    [InlineData("empty", false)]
    [InlineData("empty", true)]
    public async Task Unusable_resolver_results_preserve_public_error_codes(string kind, bool pending)
    {
        IReadOnlyList<IPAddress> addresses = kind switch
        {
            "null" => null!,
            "null-entry" => [null!],
            _ => [],
        };
        await AssertFailureAsync(addresses, pending,
            kind == "empty" ? SandboxErrorCode.PermissionDenied : SandboxErrorCode.HostFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_enumeration_failures_preserve_public_host_failure(bool pending)
    {
        var addresses = new DnsSnapshotFixture.ObservedAddresses(
            [IPAddress.Parse("93.184.216.34")], new InvalidOperationException("local source failure"));
        await AssertFailureAsync(addresses, pending, SandboxErrorCode.HostFailure);
        Assert.Equal(1, addresses.Enumerations);
    }

    [Fact]
    public async Task Cancellation_during_resolution_prevents_snapshot_enumeration()
    {
        using var fixture = new CompletedDnsFixture();
        var scenario = fixture.CreateScenario();
        using var context = scenario.Context;
        using var cancellation = new CancellationTokenSource();
        var addresses = new DnsSnapshotFixture.ObservedAddresses([IPAddress.Parse("93.184.216.34")]);
        var calls = 0;
        SafeDnsResolver resolver = (_, _) =>
        {
            calls++;
            cancellation.Cancel();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
        };
        var error = await Assert.ThrowsAsync<SandboxRuntimeException>(() =>
            fixture.Start(context, resolver, cancellation.Token).AsTask());

        Assert.Equal(SandboxErrorCode.Cancelled, error.Error.Code);
        Assert.Equal(1, calls);
        Assert.Equal(0, addresses.Enumerations);
        Assert.Equal(SandboxErrorCode.Cancelled, Assert.Single(scenario.Audit.Events).ErrorCode);
        Assert.Equal(0, context.Budget.NetworkBytesRead);
    }

    private static async Task AssertFailureAsync(
        IReadOnlyList<IPAddress> addresses, bool pending, SandboxErrorCode expected)
    {
        using var fixture = new CompletedDnsFixture();
        var scenario = fixture.CreateScenario();
        using var context = scenario.Context;
        var completion = new TaskCompletionSource<IReadOnlyList<IPAddress>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending)
        {
            completion.SetResult(addresses);
        }
        var request = fixture.Start(context, (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(completion.Task)).AsTask();
        if (pending)
        {
            Assert.False(request.IsCompleted);
            completion.SetResult(addresses);
        }

        var error = await Assert.ThrowsAsync<SandboxRuntimeException>(() => request);
        Assert.Equal(expected, error.Error.Code);
        Assert.Equal(expected, Assert.Single(scenario.Audit.Events).ErrorCode);
        Assert.Equal(0, context.Budget.NetworkBytesRead);
    }
}
