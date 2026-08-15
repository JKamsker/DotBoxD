using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsResolutionTests
{
    [Fact]
    public async Task Http_get_reports_empty_dns_results_as_dns_resolution_failure()
    {
        var result = await ExecuteAsync(StaticDns());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal("net.http.get denied: DNS resolution returned no addresses", result.Error.SafeMessage);
    }

    [Fact]
    public async Task Http_get_reports_private_dns_addresses_as_private_network_denial()
    {
        var result = await ExecuteAsync(StaticDns(IPAddress.Loopback));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal("net.http.get denied: private network targets are not allowed", result.Error.SafeMessage);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(SafeDnsResolver dnsResolver)
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("unexpected transport call"),
            dnsResolver: dnsResolver);
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }
}
