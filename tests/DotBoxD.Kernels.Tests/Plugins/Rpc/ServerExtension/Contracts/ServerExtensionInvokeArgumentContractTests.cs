using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionInvokeArgumentContractTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task InvokeServerExtensionAsync_rejects_null_single_argument_at_public_boundary()
        => await AssertRejectsNullArgumentAsync(
            CreateSingleArgumentPackage(),
            [null!]);

    [Fact]
    public async Task InvokeServerExtensionAsync_rejects_null_multi_argument_at_public_boundary()
        => await AssertRejectsNullArgumentAsync(
            CreateTwoArgumentPackage(),
            [null!, SandboxValue.FromInt32(2)]);

    private static async Task AssertRejectsNullArgumentAsync(
        PluginPackage package,
        SandboxValue[] arguments)
    {
        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => kernel.InvokeServerExtensionAsync(arguments).AsTask());

        Assert.Equal("arguments", ex.ParamName);
    }

    private static PluginPackage CreateSingleArgumentPackage()
        => CreatePackage(
            "single-argument",
            "Echo",
            [new Parameter("value", SandboxType.I32)],
            new ReturnStatement(new VariableExpression("value", Span), Span));

    private static PluginPackage CreateTwoArgumentPackage()
        => CreatePackage(
            "two-argument",
            "Add",
            [new Parameter("left", SandboxType.I32), new Parameter("right", SandboxType.I32)],
            new ReturnStatement(new VariableExpression("left", Span), Span));

    private static PluginPackage CreatePackage(
        string pluginId,
        string entrypoint,
        IReadOnlyList<Parameter> parameters,
        Statement returnStatement)
    {
        var function = new SandboxFunction(
            entrypoint,
            IsEntrypoint: true,
            parameters,
            SandboxType.I32,
            [returnStatement]);
        var module = new SandboxModule(
            pluginId,
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = pluginId, ["kernel"] = entrypoint });
        var manifest = new PluginManifest(
            pluginId,
            entrypoint,
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [])
        {
            RpcEntrypoint = entrypoint
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints(entrypoint, entrypoint));
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
