using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Guards finding #5: a postfix/prefix <c>++</c>/<c>--</c> on a <c>long</c>/<c>double</c> local must widen the
/// literal <c>1</c> to the local's converted type, exactly like <c>total += 1</c>. Before the fix the increment
/// lowered to <c>add(var[I64], i32 1)</c>, and <c>SandboxNumericOperations.Add</c> rejects the mismatched
/// operands ("numeric operand type mismatch") at invocation. Prefix <c>++total;</c> as a statement is the same
/// as <c>total++;</c> (the value is discarded) and must lower identically.
/// </summary>
public sealed class RpcKernelIncrementWideningGenerationTests
{
    private const string LongPostfixIncrementSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-postfix-inc")]
        public sealed partial class LongPostfixIncrementKernel
        {
            public long Count(HookContext ctx)
            {
                long total = 0;
                total++;
                total++;
                total++;
                return total;
            }
        }
        """;

    private const string LongPostfixDecrementSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-postfix-dec")]
        public sealed partial class LongPostfixDecrementKernel
        {
            public long Count(HookContext ctx)
            {
                long total = 5;
                total--;
                total--;
                return total;
            }
        }
        """;

    private const string DoublePostfixIncrementSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("double-postfix-inc")]
        public sealed partial class DoublePostfixIncrementKernel
        {
            public double Count(HookContext ctx)
            {
                double total = 0;
                total++;
                total++;
                return total;
            }
        }
        """;

    private const string LongPrefixIncrementSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-prefix-inc")]
        public sealed partial class LongPrefixIncrementKernel
        {
            public long Count(HookContext ctx)
            {
                long total = 0;
                ++total;
                ++total;
                return total;
            }
        }
        """;

    [Fact]
    public async Task Long_postfix_increment_widens_the_literal_to_the_locals_type()
        => Assert.Equal(3L, await RunLongAsync(LongPostfixIncrementSource, "Sample.LongPostfixIncrementPluginPackage"));

    [Fact]
    public async Task Long_postfix_decrement_widens_the_literal_to_the_locals_type()
        => Assert.Equal(3L, await RunLongAsync(LongPostfixDecrementSource, "Sample.LongPostfixDecrementPluginPackage"));

    [Fact]
    public async Task Double_postfix_increment_widens_the_literal_to_the_locals_type()
        => Assert.Equal(2.0, await RunDoubleAsync(DoublePostfixIncrementSource, "Sample.DoublePostfixIncrementPluginPackage"));

    [Fact]
    public async Task Long_prefix_increment_widens_the_literal_to_the_locals_type()
        => Assert.Equal(2L, await RunLongAsync(LongPrefixIncrementSource, "Sample.LongPrefixIncrementPluginPackage"));

    private static async Task<long> RunLongAsync(string source, string factoryTypeName)
        => Assert.IsType<I64Value>(await InvokeAsync(source, factoryTypeName)).Value;

    private static async Task<double> RunDoubleAsync(string source, string factoryTypeName)
        => Assert.IsType<F64Value>(await InvokeAsync(source, factoryTypeName)).Value;

    private static async Task<SandboxValue> InvokeAsync(string source, string factoryTypeName)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(source, factoryTypeName);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        return await kernel.InvokeServerExtensionAsync([]);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
