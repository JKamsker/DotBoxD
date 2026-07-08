using System.Net;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network.PublicContract;

public sealed class SafeHttpPublicContractTests
{
    [Fact]
    public async Task GetTextAsync_rejects_null_context_with_public_parameter_name()
    {
        using var invoker = FakeInvoker("ok");

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeHttpClient.GetTextAsync(
                null!,
                new SandboxUri("https://api.example.com/config"),
                invoker,
                StaticDns(IPAddress.Parse("93.184.216.34")),
                CancellationToken.None));

        Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task GetTextAsync_rejects_null_uri_with_public_parameter_name()
    {
        using var invoker = FakeInvoker("ok");
        var context = CreateContext();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeHttpClient.GetTextAsync(
                context,
                null!,
                invoker,
                StaticDns(IPAddress.Parse("93.184.216.34")),
                CancellationToken.None));

        Assert.Equal("uri", ex.ParamName);
    }

    [Fact]
    public void String_response_invoker_rejects_null_response_with_public_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SafeInMemoryHttpMessageInvoker((string)null!));

        Assert.Equal("response", ex.ParamName);
    }

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
