using System.Reflection;
using System.Runtime.Versioning;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientPlatformCompatibilityAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_platform_compatibility_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "WindowsEchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(int), typeof(System.Threading.CancellationToken)])!;

        var attribute = Assert.Single(method.GetCustomAttributes<SupportedOSPlatformAttribute>());

        Assert.Equal("windows", attribute.PlatformName);
    }

    [Fact]
    public void Service_backed_receiver_method_extension_preserves_platform_compatibility_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]" +
            "\n        public global::System.Threading.Tasks.ValueTask<int> @WindowsEchoValue(");
    }

    [Fact]
    public void Direct_receiver_method_extension_preserves_platform_compatibility_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]" +
            "\n    public static int @WindowsEcho(");
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      NormalizeLineEndings(source).Contains(expectedSource, StringComparison.Ordinal));

    private static string NormalizeLineEndings(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string ServiceBackedSource = """
        #nullable enable
        using System.Runtime.Versioning;
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
            [SupportedOSPlatform("windows")]
            ValueTask<int> WindowsEchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "WindowsEchoValue")]
            public int WindowsEcho(int value, HookContext ctx) => value;
        }
        """;

    private const string DirectExtensionSource = """
        #nullable enable
        using System.Runtime.Versioning;
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

        [ServerExtension(typeof(IRemoteControl), "direct-echo")]
        public sealed partial class EchoKernel
        {
            [SupportedOSPlatform("windows")]
            [ServerExtensionMethod(typeof(IRemoteControl), "WindowsEcho")]
            public int WindowsEcho(int value, HookContext ctx) => value;
        }
        """;
}
