using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class HookChainPlatformCompatibilityAttributeSurpriseTests
{
    private const string SupportedOsPlatformAttributeName = "System.Runtime.Versioning.SupportedOSPlatformAttribute";

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Remote_Run_preserves_or_rejects_platform_restricted_event_types()
    {
        var result = CompileWithGenerator("""
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            [SupportedOSPlatform("windows")]
            public sealed record WindowsOnlyEvent(string TargetId, int Damage);

            public sealed record PortableEvent(string TargetId, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    hooks.On<WindowsOnlyEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "windows"));
                    hooks.On<PortableEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "portable"));
                }
            }
            """);

        var dbxkDiagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal))
            .ToArray();
        if (dbxkDiagnostics.Length > 0)
        {
            Assert.All(dbxkDiagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
            return;
        }

        var windowsPackage = FindGeneratedType(result.OutputCompilation, "WindowsOnlyEvent", "PluginPackage");
        var portablePackage = FindGeneratedType(result.OutputCompilation, "PortableEvent", "PluginPackage");
        var windowsInterceptor = FindGeneratedMethod(result.OutputCompilation, "WindowsOnlyEvent");
        var portableInterceptor = FindGeneratedMethod(result.OutputCompilation, "PortableEvent");

        AssertSupportedOnWindows(windowsPackage);
        AssertSupportedOnWindows(windowsInterceptor);
        AssertNotSupportedOnWindows(portablePackage);
        AssertNotSupportedOnWindows(portableInterceptor);
    }

    private static void AssertSupportedOnWindows(ISymbol symbol)
        => Assert.Contains(
            symbol.GetAttributes(),
            attribute => attribute.AttributeClass?.ToDisplayString() == SupportedOsPlatformAttributeName &&
                         attribute.ConstructorArguments is [{ Value: "windows" }]);

    private static void AssertNotSupportedOnWindows(ISymbol symbol)
        => Assert.DoesNotContain(
            symbol.GetAttributes(),
            attribute => attribute.AttributeClass?.ToDisplayString() == SupportedOsPlatformAttributeName);

    private static INamedTypeSymbol FindGeneratedType(Compilation compilation, string marker, string nameSuffix)
        => Assert.Single(
            AllTypes(compilation.Assembly.GlobalNamespace),
            type => type.Name.EndsWith(nameSuffix, StringComparison.Ordinal) &&
                    HasGeneratedMarker(type, marker));

    private static IMethodSymbol FindGeneratedMethod(Compilation compilation, string marker)
        => Assert.Single(
            AllTypes(compilation.Assembly.GlobalNamespace)
                .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>()),
            method => method.Name.StartsWith("Intercept_", StringComparison.Ordinal) &&
                      HasGeneratedMarker(method, marker));

    private static bool HasGeneratedMarker(ISymbol symbol, string marker)
        => symbol.DeclaringSyntaxReferences.Any(reference =>
            IsGeneratedSource(reference.SyntaxTree) &&
            reference.GetSyntax().ToFullString().Contains(marker, StringComparison.Ordinal));

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var childNamespace in @namespace.GetNamespaceMembers())
        {
            foreach (var type in AllTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static GeneratedCompilation CompileWithGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainPlatformMetadataTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        return new GeneratedCompilation(
            outputCompilation,
            generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray());
    }

    private static bool IsGeneratedSource(SyntaxTree syntaxTree)
        => syntaxTree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record GeneratedCompilation(Compilation OutputCompilation, IReadOnlyList<Diagnostic> Diagnostics);
}
