using System.Net;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network.GrantValidation;

public sealed class SafeHttpMissingResponseLimitTests
{
    [Fact]
    public async Task Direct_http_grant_missing_maxResponseBytes_fails_closed()
    {
        var policy = new SandboxPolicy(
            "missing-http-response-limit",
            SandboxEffects.Pure | SandboxEffect.Network,
            [
                new CapabilityGrant(
                    "net.http.get",
                    new Dictionary<string, string>
                    {
                        ["allowedHosts"] = "api.example.com"
                    })
            ],
            new ResourceLimits(MaxNetworkBytesRead: 1024, MaxNetworkBytesWritten: 1024));
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                FakeInvoker("ok"),
                StaticDns(IPAddress.Parse("93.184.216.34")),
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }
}
