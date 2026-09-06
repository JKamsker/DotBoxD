using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class RemoteRunLocalHookResultAttributeIdentitySurpriseTests
{
    [Fact]
    public void Foreign_hook_result_marker_does_not_enable_omitted_projection_field_zero_fill()
    {
        var foreignHookResultAttribute = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace DotBoxD.Abstractions;

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class HookResultAttribute : System.Attribute
            {
            }
            """,
            "ForeignHookResultAttributes")
            .WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignHookResultAttributes"]));

        var diagnostics = ChainGeneratorDiagnostics(foreignHookResultAttribute);

        var diagnostic = Assert.Single(
            diagnostics,
            candidate => string.Equals(candidate.Id, "DBXK111", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static ImmutableArray<Diagnostic> ChainGeneratorDiagnostics(MetadataReference foreignHookResultAttribute)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDForeignHookResultProjectionTest",
            [CSharpSyntaxTree.ParseText(Source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(foreignHookResultAttribute),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        return PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.RunGenerators(compilation).GetRunResult()).Diagnostics;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private const string Source = """
        extern alias ForeignHookResultAttributes;

        using DotBoxD.Plugins.Runtime;

        namespace Regression;

        public sealed record Encounter(int Distance, string Name);

        [ForeignHookResultAttributes::DotBoxD.Abstractions.HookResult]
        public sealed class ForeignMarkedProjection
        {
            public string Name { get; init; } = string.Empty;
            public int Rank { get; init; } = 73;
        }

        public static class ForeignMarkedProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Encounter>().Where(e => e.Distance <= 4)
                    .Select(e => new ForeignMarkedProjection { Name = e.Name })
                    .RunLocal((projection, context) => { });
        }
        """;
}
