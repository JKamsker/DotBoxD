using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientRequestHelperCollisionTests
{
    [Theory]
    [InlineData("WriteKernelRpcValue0")]
    [InlineData("WriteKernelRpcValue5")]
    public void Generated_request_writers_avoid_service_method_names(string methodName)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ListServiceSource(methodName));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generated_DateTime_writer_avoids_fixed_service_method_name()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(DateTimeServiceSource);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void List_request_emits_only_its_required_writer()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(ListServiceSource("Echo"));
        var requestWriters = result.GeneratedTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(static method => method.Identifier.ValueText.StartsWith("WriteKernelRpcValue", StringComparison.Ordinal))
            .Select(static method => method.Identifier.ValueText)
            .ToArray();

        Assert.Equal(["WriteKernelRpcValue0"], requestWriters);
        Assert.DoesNotContain(
            result.GeneratedTrees.SelectMany(static tree => tree.GetRoot().DescendantNodes())
                .OfType<MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "DateTimeToWireOffset");
    }

    [Fact]
    public void Generated_direct_request_writer_avoids_extension_method_name()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(DirectListSource);
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(DirectListSource);
        var writer = Assert.Single(
            result.GeneratedTrees
                .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()),
            static method => method.Modifiers.Any(SyntaxKind.PrivateKeyword) &&
                method.Identifier.ValueText.StartsWith("WriteKernelRpcValue", StringComparison.Ordinal));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("WriteKernelRpcValue1", writer.Identifier.ValueText);
    }

    private static string ListServiceSource(string methodName)
        => $$"""
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            public interface IListService
            {
                int {{methodName}}(List<int> values);
            }

            [ServerExtension("list-request", typeof(IListService))]
            public sealed partial class ListKernel
            {
                public int {{methodName}}(List<int> values, HookContext context)
                {
                    return 1;
                }
            }
            """;

    private const string DateTimeServiceSource = """
        using System;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;

        namespace Sample;

        public interface IDateTimeService
        {
            int DateTimeToWireOffset(DateTime value);
        }

        [ServerExtension("datetime-request", typeof(IDateTimeService))]
        public sealed partial class DateTimeKernel
        {
            public int DateTimeToWireOffset(DateTime value, HookContext context)
            {
                return 1;
            }
        }
        """;

    private const string DirectListSource = """
        using System.Collections.Generic;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
        }

        [ServerExtension(typeof(IRemoteControl), "direct-list-request")]
        public sealed partial class ListKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int WriteKernelRpcValue0(List<int> values, HookContext context)
            {
                return 1;
            }
        }

        public static class Probe
        {
            public static int Invoke(RemoteControl remote, List<int> values)
                => remote.WriteKernelRpcValue0(values);
        }
        """;
}
