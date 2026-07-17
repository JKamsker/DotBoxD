using System.Diagnostics;
using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class NetworkDeadlineTests
{
    [Fact]
    public async Task Http_get_caps_dns_timeout_to_remaining_wall_time()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("unused"), dnsResolver: SlowDns());
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = LongRequestShortWallPolicy();
        var plan = await host.PrepareAsync(module, policy);
        var elapsed = Stopwatch.StartNew();

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        elapsed.Stop();
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {elapsed.Elapsed}");
        AssertSingleTimeoutAudit(result);
    }

    [Fact]
    public async Task Http_get_caps_send_timeout_to_remaining_wall_time()
    {
        var host = SandboxTestHost.Create(networkInvoker: SlowInvoker(), dnsResolver: StaticDns(IPAddress.Parse("93.184.216.34")));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = LongRequestShortWallPolicy();
        var plan = await host.PrepareAsync(module, policy);
        var elapsed = Stopwatch.StartNew();

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        elapsed.Stop();
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {elapsed.Elapsed}");
        AssertSingleTimeoutAudit(result);
    }

    [Fact]
    public async Task Http_get_records_zero_bytes_when_request_timeout_wins_before_wall_deadline()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("unused"), dnsResolver: SlowDns());
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, timeout: TimeSpan.FromMilliseconds(50))
            .WithWallTime(TimeSpan.FromSeconds(2))
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        var audit = AssertSingleTimeoutAudit(result);
        Assert.Equal(0, audit.Bytes);
        Assert.Equal("0", audit.Fields!["bytesRead"]);
        var bytesWritten = long.Parse(
            audit.Fields["bytesWritten"],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(bytesWritten > 0);
        Assert.Equal(result.ResourceUsage.NetworkBytesWritten, bytesWritten);
    }

    private static SandboxAuditEvent AssertSingleTimeoutAudit(SandboxExecutionResult result)
    {
        var audit = Assert.Single(
            result.AuditEvents,
            audit => audit is
            {
                BindingId: "net.http.get",
                Success: false,
                ErrorCode: SandboxErrorCode.Timeout
            });
        return audit;
    }

    private static SandboxPolicy LongRequestShortWallPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, timeout: TimeSpan.FromSeconds(5))
            .WithWallTime(TimeSpan.FromMilliseconds(50))
            .WithFuel(5_000)
            .Build();

    private static SafeDnsResolver SlowDns()
        => async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return [IPAddress.Parse("93.184.216.34")];
        };

    private static SafeInMemoryHttpMessageInvoker SlowInvoker()
        => new("late", responseDelay: TimeSpan.FromSeconds(5));
}
