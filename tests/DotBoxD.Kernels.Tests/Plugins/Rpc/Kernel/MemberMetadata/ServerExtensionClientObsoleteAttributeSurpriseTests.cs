using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientObsoleteAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_obsolete_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "LegacyEchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(int), typeof(System.Threading.CancellationToken)])!;

        var attribute = Assert.Single(method.GetCustomAttributes<ObsoleteAttribute>());

        Assert.Equal("Use EchoAsync", attribute.Message);
        Assert.False(attribute.IsError);
    }

    [Fact]
    public void Service_backed_receiver_method_extension_preserves_obsolete_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "[global::System.ObsoleteAttribute(\"Use EchoAsync\")]");
        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "LegacyEchoValue(int @value, global::System.Threading.CancellationToken @cancellationToken = default)");
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private const string ServiceBackedSource = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface IEchoService
        {
            [Obsolete("Use EchoAsync")]
            ValueTask<int> LegacyEchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "LegacyEchoValue")]
            public int LegacyEcho(int value, HookContext ctx) => value;
        }
        """;
}
