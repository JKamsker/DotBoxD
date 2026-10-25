using System.Reflection;
using System.Runtime.Versioning;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientPlatformGuardAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_platform_guard_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var unsupportedAssembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedUnsupportedSource);
        var unsupportedClient = unsupportedAssembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;

        AssertPlatformGuard<SupportedOSPlatformGuardAttribute>(
            client.GetMethod("IsWindows", BindingFlags.Public | BindingFlags.Instance)!);
        AssertPlatformGuard<UnsupportedOSPlatformGuardAttribute>(
            unsupportedClient.GetMethod("IsNotWindows", BindingFlags.Public | BindingFlags.Instance)!);
    }

    [Fact]
    public void Service_backed_receiver_method_extensions_preserve_platform_guard_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.SupportedOSPlatformGuardAttribute(\"windows\")]" +
            "\n        public bool @IsWindowsValue(");
        AssertGeneratedSourceContains(
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedUnsupportedSource),
            "EchoKernelServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute(\"windows\")]" +
            "\n        public bool @IsNotWindowsValue(");
    }

    [Fact]
    public void Direct_receiver_method_extensions_preserve_platform_guard_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.SupportedOSPlatformGuardAttribute(\"windows\")]" +
            "\n    public static bool @IsWindows(");
        AssertGeneratedSourceContains(
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionUnsupportedSource),
            "EchoKernelDirectServerExtensionClientExtensions",
            "[global::System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute(\"windows\")]" +
            "\n    public static bool @IsNotWindows(");
    }

    private static void AssertPlatformGuard<TAttribute>(MethodInfo method)
        where TAttribute : OSPlatformAttribute
        => Assert.Contains(method.GetCustomAttributes<TAttribute>(), attribute =>
            attribute.PlatformName == "windows");

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
            [SupportedOSPlatformGuard("windows")]
            bool IsWindows();
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "IsWindowsValue")]
            public bool IsWindows(HookContext ctx) => true;
        }
        """;

    private static string ServiceBackedUnsupportedSource => ServiceBackedSource
        .Replace("SupportedOSPlatformGuard", "UnsupportedOSPlatformGuard", StringComparison.Ordinal)
        .Replace("IsWindows", "IsNotWindows", StringComparison.Ordinal);

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
            [SupportedOSPlatformGuard("windows")]
            [ServerExtensionMethod(typeof(IRemoteControl), "IsWindows")]
            public bool IsWindows(HookContext ctx) => true;
        }
        """;

    private static string DirectExtensionUnsupportedSource => DirectExtensionSource
        .Replace("SupportedOSPlatformGuard", "UnsupportedOSPlatformGuard", StringComparison.Ordinal)
        .Replace("IsWindows", "IsNotWindows", StringComparison.Ordinal);
}
