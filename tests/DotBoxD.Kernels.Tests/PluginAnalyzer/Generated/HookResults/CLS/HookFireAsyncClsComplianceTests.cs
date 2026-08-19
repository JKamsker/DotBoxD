using System.Collections.Immutable;
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
        Assert.Contains("[global::System.CLSCompliant(false)]", fireAsyncTrees[0].ToString(), StringComparison.Ordinal);

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

    [Fact]
    public void Generated_fire_async_extensions_ignore_extern_aliased_lookalike_cls_compliance_attribute()
    {
        var foreignClsCompliant = CompileForeignClsCompliantAttribute()
            .WithAliases(ImmutableArray.Create("Foreign"));
        var compilation = CreateCompilation(
            """
            extern alias Foreign;

            using DotBoxD.Abstractions;

            [assembly: Foreign::System.CLSCompliant(true)]

            namespace Sample;

            [Hook("damage", typeof(DamageResult))]
            public sealed record DamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """,
            foreignClsCompliant);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: s_parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var fireAsyncTree = Assert.Single(driver.GetRunResult().GeneratedTrees.Where(
            static tree => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal)));
        Assert.DoesNotContain("[global::System.CLSCompliant(false)]", fireAsyncTree.ToString(), StringComparison.Ordinal);
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

    private static MetadataReference CompileForeignClsCompliantAttribute()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignClsCompliant",
            [CSharpSyntaxTree.ParseText("""
                namespace System;

                [AttributeUsage(AttributeTargets.Assembly)]
                public sealed class CLSCompliantAttribute : Attribute
                {
                    public CLSCompliantAttribute(bool isCompliant)
                    {
                    }
                }
                """, s_parseOptions)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source, params MetadataReference[] additionalReferences)
        => CSharpCompilation.Create(
            "DotBoxDFireAsyncClsComplianceTest",
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(HookRegistry).Assembly.Location))
                .Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(static reference => MetadataReference.CreateFromFile(reference));
    }
}
