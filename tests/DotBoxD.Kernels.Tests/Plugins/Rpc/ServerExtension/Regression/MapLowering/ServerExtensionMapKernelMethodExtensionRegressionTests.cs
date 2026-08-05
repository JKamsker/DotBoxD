using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionMapKernelMethodExtensionRegressionTests
{
    [Fact]
    public void Map_receiver_KernelMethod_extension_named_ContainsKey_is_not_lowered_as_map_intrinsic()
    {
        var staticCall = PluginAnalyzerGeneratedPackageFactory.Create(
            Source("MapKernelMethods.ContainsKey(values, 42)", "MapExtensionStaticKernel", "map-extension-static"),
            "Sample.MapExtensionStaticPluginPackage");
        Assert.DoesNotContain(MapIntrinsicCalls(staticCall), call => call.Name == "map.containsKey");

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            Source("values.ContainsKey(42)", "MapExtensionSyntaxKernel", "map-extension-syntax"));

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("map key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var extensionCall = PluginAnalyzerGeneratedPackageFactory.Create(
            Source("values.ContainsKey(42)", "MapExtensionSyntaxKernel", "map-extension-syntax"),
            "Sample.MapExtensionSyntaxPluginPackage");
        Assert.DoesNotContain(MapIntrinsicCalls(extensionCall), call => call.Name == "map.containsKey");
    }

    private static IEnumerable<CallExpression> MapIntrinsicCalls(PluginPackage package)
        => Assert.Single(package.Module.Functions).Body.SelectMany(Calls);

    private static IEnumerable<CallExpression> Calls(Statement statement)
        => statement switch
        {
            AssignmentStatement assignment => Calls(assignment.Value),
            ExpressionStatement expression => Calls(expression.Value),
            ReturnStatement returned => Calls(returned.Value),
            IfStatement branch => Calls(branch.Condition)
                .Concat(branch.Then.SelectMany(Calls))
                .Concat(branch.Else.SelectMany(Calls)),
            WhileStatement loop => Calls(loop.Condition).Concat(loop.Body.SelectMany(Calls)),
            ForRangeStatement loop => Calls(loop.Start)
                .Concat(Calls(loop.End))
                .Concat(loop.Body.SelectMany(Calls)),
            _ => []
        };

    private static IEnumerable<CallExpression> Calls(Expression expression)
    {
        switch (expression)
        {
            case CallExpression call:
                yield return call;
                foreach (var nested in call.Arguments.SelectMany(Calls))
                {
                    yield return nested;
                }

                break;
            case BinaryExpression binary:
                foreach (var nested in Calls(binary.Left).Concat(Calls(binary.Right)))
                {
                    yield return nested;
                }

                break;
            case UnaryExpression unary:
                foreach (var nested in Calls(unary.Operand))
                {
                    yield return nested;
                }

                break;
        }
    }

    private static string Source(string invocation, string kernelName, string pluginId)
        => $$"""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public static class MapKernelMethods
            {
                [KernelMethod]
                public static bool ContainsKey(this Dictionary<string, int> values, int key)
                {
                    return key == 42;
                }
            }

            [ServerExtension("{{pluginId}}")]
            public sealed partial class {{kernelName}}
            {
                public int Check(Dictionary<string, int> values, HookContext ctx)
                {
                    if ({{invocation}})
                    {
                        return 1;
                    }

                    return 0;
                }
            }
            """;
}
