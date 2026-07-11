using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionInvokeArgumentContractTests
{
    private const string SingleArgumentSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Plugins;

        namespace Sample;

        [ServerExtension("single-argument")]
        public sealed partial class SingleArgumentKernel
        {
            public int Echo(int value, HookContext ctx) => value;
        }
        """;

    private const string TwoArgumentSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Plugins;

        namespace Sample;

        [ServerExtension("two-argument")]
        public sealed partial class TwoArgumentKernel
        {
            public int Add(int left, int right, HookContext ctx) => left + right;
        }
        """;

    [Fact]
    public async Task InvokeServerExtensionAsync_rejects_null_single_argument_at_public_boundary()
        => await AssertRejectsNullArgumentAsync(
            SingleArgumentSource,
            "Sample.SingleArgumentPluginPackage",
            [null!]);

    [Fact]
    public async Task InvokeServerExtensionAsync_rejects_null_multi_argument_at_public_boundary()
        => await AssertRejectsNullArgumentAsync(
            TwoArgumentSource,
            "Sample.TwoArgumentPluginPackage",
            [null!, SandboxValue.FromInt32(2)]);

    private static async Task AssertRejectsNullArgumentAsync(
        string source,
        string packageName,
        SandboxValue[] arguments)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(source, packageName);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => kernel.InvokeServerExtensionAsync(arguments).AsTask());

        Assert.Equal("arguments", ex.ParamName);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
