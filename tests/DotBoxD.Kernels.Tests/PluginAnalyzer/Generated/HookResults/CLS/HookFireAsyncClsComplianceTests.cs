using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncClsComplianceTests
{
    private static readonly HashSet<string> s_clsDiagnosticIds = ["CS3001", "CS3002", "CS3003"];
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_fire_async_extensions_do_not_emit_cls_diagnostics()
    {
        var compilation = CreateCompilation("""
            #nullable enable
            using System;
            using DotBoxD.Abstractions;

            [assembly: CLSCompliant(true)]

            namespace Sample;

            [Hook("damage", typeof(DamageResult))]
            public sealed record DamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: s_parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        if (TryAssertFocusedClsFailClosed(generatorDiagnostics, runResult))
        {
            return;
        }

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var fireAsyncTrees = runResult.GeneratedTrees
            .Where(static tree => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(fireAsyncTrees);

        var generatedTrees = runResult.GeneratedTrees.ToHashSet();
        var diagnostics = outputCompilation.GetDiagnostics();
        var userClsDiagnostics = ClsDiagnostics(diagnostics)
            .Where(diagnostic => diagnostic.Location.SourceTree is null ||
                                 !generatedTrees.Contains(diagnostic.Location.SourceTree))
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.Empty(userClsDiagnostics);

        var generatedClsDiagnostics = ClsDiagnostics(diagnostics)
            .Where(diagnostic => diagnostic.Location.SourceTree is not null &&
                                 generatedTrees.Contains(diagnostic.Location.SourceTree))
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(
            generatedClsDiagnostics.Length == 0,
            "Generated FireAsync sources should not emit CLS diagnostics:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, generatedClsDiagnostics));
    }

    private static bool TryAssertFocusedClsFailClosed(
        IEnumerable<Diagnostic> generatorDiagnostics,
        GeneratorDriverRunResult runResult)
    {
        var failClosed = generatorDiagnostics.FirstOrDefault(static diagnostic =>
            diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage().Contains("CLS", StringComparison.OrdinalIgnoreCase));
        if (failClosed is null)
        {
            return false;
        }

        Assert.DoesNotContain(
            runResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal));
        return true;
    }

    private static IEnumerable<Diagnostic> ClsDiagnostics(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Where(static diagnostic => s_clsDiagnosticIds.Contains(diagnostic.Id));

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDFireAsyncClsComplianceTest",
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(HookRegistry).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(static reference => MetadataReference.CreateFromFile(reference));
    }
}
