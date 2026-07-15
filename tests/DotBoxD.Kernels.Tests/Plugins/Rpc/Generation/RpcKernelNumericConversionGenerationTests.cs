using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelNumericConversionGenerationTests
{
    private const string LongLiteralSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-literal")]
        public sealed partial class LongLiteralKernel
        {
            public long Zero(HookContext ctx)
            {
                return 0;
            }
        }
        """;

    private const string LongParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-parameter")]
        public sealed partial class LongParameterKernel
        {
            public long Echo(int value, HookContext ctx)
            {
                return value;
            }
        }
        """;

    private const string FloatLiteralSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("float-literal")]
        public sealed partial class FloatLiteralKernel
        {
            public float Half(HookContext ctx)
            {
                return 1.5f;
            }
        }
        """;

    private const string FloatParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("float-parameter")]
        public sealed partial class FloatParameterKernel
        {
            public float Echo(int value, HookContext ctx)
            {
                return value;
            }
        }
        """;

    private const string FloatExpressionParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("float-expression-parameter")]
        public sealed partial class FloatExpressionParameterKernel
        {
            public float Echo(int value, HookContext ctx) => value;
        }
        """;

    [Fact]
    public async Task Generated_rpc_kernel_preserves_implicit_numeric_literal_conversions()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            LongLiteralSource,
            "Sample.LongLiteralPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(0L, Assert.IsType<I64Value>(result).Value);
    }

    [Fact]
    public async Task Generated_rpc_kernel_preserves_implicit_numeric_variable_conversions()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            LongParameterSource,
            "Sample.LongParameterPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(42)]);

        Assert.Equal(42L, Assert.IsType<I64Value>(result).Value);
    }

    [Fact]
    public async Task Generated_rpc_kernel_preserves_float_literals()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            FloatLiteralSource,
            "Sample.FloatLiteralPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(1.5, Assert.IsType<F64Value>(result).Value);
    }

    [Fact]
    public void Generated_rpc_kernel_rejects_block_body_int_to_float_variable_conversions()
    {
        AssertUnsupportedSingleReturnConversion(FloatParameterSource);
    }

    [Fact]
    public void Generated_rpc_kernel_rejects_expression_body_int_to_float_variable_conversions()
    {
        AssertUnsupportedSingleReturnConversion(FloatExpressionParameterSource);
    }

    private static void AssertUnsupportedSingleReturnConversion(string source)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("return expression", StringComparison.OrdinalIgnoreCase));
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
