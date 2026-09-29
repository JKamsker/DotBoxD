using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypePlatformCompatibilityAttributeSurpriseTests
{
    private const string SupportedWindowsAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Fact]
    public void Service_backed_generated_client_preserves_platform_compatibility_service_attribute()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(ServiceBackedSource);

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            SupportedWindowsAttribute +
            "\npublic sealed class EchoKernelServerExtensionClient : global::Sample.IEchoService");
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "PortableKernelServerExtensionClient",
            "public sealed class PortableKernelServerExtensionClient : global::Sample.IPortableService");
        var portableClientSource = Assert.Single(
            result.GeneratedSources,
            source => source.Contains("class PortableKernelServerExtensionClient", StringComparison.Ordinal));
        Assert.DoesNotContain(SupportedWindowsAttribute, portableClientSource, StringComparison.Ordinal);

        AssertGeneratedClientUseReportsPlatformDiagnostic(result.OutputCompilation);
    }

    private static void AssertGeneratedClientUseReportsPlatformDiagnostic(Compilation compilation)
    {
        var consumerTree = CSharpSyntaxTree.ParseText(ConsumerSource, RpcMemberMetadataGeneratorHarness.ParseOptions);
        var consumerCompilation = compilation.AddSyntaxTrees(consumerTree)
            .WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                compilation.Options.SpecificDiagnosticOptions.SetItem("CA1416", ReportDiagnostic.Warn)));
        var diagnostics = consumerCompilation.WithAnalyzers(PlatformCompatibilityAnalyzers())
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "CA1416" &&
                          diagnostic.Location.SourceTree == consumerTree &&
                          DiagnosticLine(diagnostic).Contains("EchoKernelServerExtensionClient client", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "CA1416" &&
                          diagnostic.Location.SourceTree == consumerTree &&
                          DiagnosticLine(diagnostic).Contains("PortableKernelServerExtensionClient client", StringComparison.Ordinal));
    }

    private static ImmutableArray<DiagnosticAnalyzer> PlatformCompatibilityAnalyzers()
    {
        var analyzerPath = Directory
            .EnumerateFiles(DotNetRoot(), "Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll", SearchOption.AllDirectories)
            .OrderByDescending(static path => path, StringComparer.Ordinal)
            .First();
        var reference = new AnalyzerFileReference(analyzerPath, new AnalyzerAssemblyLoader());
        return reference.GetAnalyzers(LanguageNames.CSharp);
    }

    private static string DotNetRoot()
    {
        var sharedFrameworkDirectory = Directory.GetParent(typeof(object).Assembly.Location)!;
        return Directory.GetParent(Directory.GetParent(sharedFrameworkDirectory.FullName)!.FullName)!.FullName;
    }

    private static string DiagnosticLine(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var text = diagnostic.Location.SourceTree!.GetText();
        return text.Lines[span.StartLinePosition.Line].ToString();
    }

    private sealed class AnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath)
            => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private const string ConsumerSource = """
        #nullable enable
        using System.Runtime.Versioning;

        [assembly: SupportedOSPlatform("linux")]

        namespace Sample;

        public static class GeneratedClientConsumer
        {
            public static void ViaWindowsClient(EchoKernelServerExtensionClient client)
            {
            }

            public static void ViaPortableClient(PortableKernelServerExtensionClient client)
            {
            }
        }
        """;

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

        [SupportedOSPlatform("windows")]
        public interface IEchoService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        public interface IPortableService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "PortableEchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }

        [ServerExtensionClient(typeof(IRemoteControl), "PortableClient")]
        [ServerExtension("portable", typeof(IPortableService))]
        public sealed partial class PortableKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;
}
