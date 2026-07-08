using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypeObsoleteAttributeTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_obsolete_service_attribute()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(ServiceBackedSource);
        var generatedDiagnostics = result.OutputCompilation.GetDiagnostics()
            .Where(diagnostic => IsGeneratedDiagnostic(diagnostic, result.GeneratedTrees))
            .ToArray();

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generatedDiagnostics, diagnostic => diagnostic.Id == "CS0618");
        AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "[global::System.ObsoleteAttribute(\"Use INew\")]");
        AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "public sealed class EchoKernelServerExtensionClient : global::Sample.IEchoService");

    }

    private static bool IsGeneratedDiagnostic(
        Diagnostic diagnostic,
        IReadOnlySet<SyntaxTree> generatedTrees)
        => diagnostic.Location.SourceTree is { } tree && generatedTrees.Contains(tree);

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private const string ServiceBackedSource = """
        #pragma warning disable CS0618
        using System;
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

        [Obsolete("Use INew")]
        public interface IEchoService
        {
            ValueTask<string> EchoAsync(int value);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public string Echo(int value, HookContext ctx) => "echo";
        }
        """;
}
