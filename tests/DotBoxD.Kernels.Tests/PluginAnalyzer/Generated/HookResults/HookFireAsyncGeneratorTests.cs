using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncGeneratorTests
{
    [Fact]
    public void FireAsync_extensions_are_internal_so_internal_hook_types_compile()
    {
        _ = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [Hook("internal.damage", typeof(DamageResult))]
            internal sealed record DamageCtx(int Damage);

            [HookResult]
            internal readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            internal static class Usage
            {
                public static async ValueTask<int> FireAsync(HookRegistry hooks)
                {
                    var result = await hooks.FireAsync(new DamageCtx(5));
                    return result?.Damage ?? 0;
                }
            }
            """);
    }

    [Fact]
    public void Invalid_hook_result_does_not_emit_fire_async_extension_errors()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("bad.damage", typeof(BadResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct BadResult(bool Success, int Damage);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK112");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0315");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_positional_hook_result_does_not_emit_fire_async_extension_errors()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("bad.damage", typeof(BadResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct BadResult
            {
                public bool Success { get; }
                public string? Reason { get; }
            }
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK112");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0315");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_hook_result_reports_contract_diagnostic_without_invalid_partial()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct GenericResult<T>(bool Success, string? Reason, T Value);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK112");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "CS0305" or "CS0246");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("GenericResult.HookResultBuilders", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_type_manual_hook_result_does_not_emit_fire_async_extension()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("bad.damage", typeof(ClassResult))]
            public sealed record DamageCtx(int Damage);

            public sealed record ClassResult(bool Success, string? Reason) : IHookResult;
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0315");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void FireAsync_default_literal_selects_cancellation_token_overload()
    {
        _ = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [Hook("default.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static async ValueTask<int> FireAsync(HookRegistry hooks)
                {
                    var result = await hooks.FireAsync(new DamageCtx(5), default);
                    return result?.Damage ?? 0;
                }
            }
            """);
    }

    [Fact]
    public void Public_hook_contracts_emit_public_cross_assembly_fire_async_extension()
    {
        var contracts = CompileContractReference("""
            using DotBoxD.Abstractions;

            namespace Contracts;

            [Hook("contract.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            """);
        var diagnostics = CompileHost(
            """
            using System.Threading.Tasks;
            using Contracts;
            using DotBoxD.Plugins.Runtime;

            namespace Host;

            public static class Usage
            {
                public static async ValueTask<int> FireAsync(HookRegistry hooks)
                {
                    var result = await hooks.FireAsync(new DamageCtx(5));
                    return result?.Damage ?? 0;
                }
            }
            """,
            contracts);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generic_hook_context_does_not_emit_unbound_type_parameter_extension()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("generic.damage", typeof(DamageResult))]
            public sealed record GenericDamageCtx<T>(T Payload);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0246");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    private static MetadataReference CompileContractReference(string source)
    {
        var compilation = CreateCompilation("Contracts", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        using var assembly = new MemoryStream();
        var emit = outputCompilation.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(assembly.ToArray());
    }

    private static IReadOnlyList<Diagnostic> CompileHost(string source, MetadataReference contractReference)
        => CreateCompilation("Host", source, contractReference)
            .GetDiagnostics()
            .ToArray();

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
        => CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
