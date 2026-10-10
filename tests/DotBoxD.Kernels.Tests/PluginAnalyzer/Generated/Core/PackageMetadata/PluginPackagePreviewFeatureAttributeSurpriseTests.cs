using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackagePreviewFeatureAttributeSurpriseTests
{
    private const string RequiresPreviewFeaturesAttribute =
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_packages_preserve_preview_feature_kernel_contracts()
    {
        var compilation = CreateCompilation("""
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [RequiresPreviewFeatures("Preview plugin package", Url = "https://example.test/preview-plugin")]
            [Plugin("sample.preview")]
            public sealed partial class PreviewDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext context) => true;

                public void Handle(DamageEvent e, HookContext context)
                    => context.Messages.Send(e.TargetId, "preview");
            }

            [Plugin("sample.portable")]
            public sealed partial class PortableDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext context) => true;

                public void Handle(DamageEvent e, HookContext context)
                    => context.Messages.Send(e.TargetId, "portable");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(2, PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()).GeneratedTrees.Length);

        AssertPreviewFeatureAttribute(
            outputCompilation.GetTypeByMetadataName("Sample.PreviewDamagePluginPackage"),
            "Preview plugin package",
            "https://example.test/preview-plugin");
        AssertNoPreviewFeatureAttribute(
            outputCompilation.GetTypeByMetadataName("Sample.PortableDamagePluginPackage"));
    }

    private static void AssertPreviewFeatureAttribute(INamedTypeSymbol? packageType, string message, string url)
    {
        Assert.NotNull(packageType);
        var attribute = Assert.Single(
            packageType.GetAttributes(),
            candidate => candidate.AttributeClass?.ToDisplayString() == RequiresPreviewFeaturesAttribute);

        Assert.Equal(message, Assert.Single(attribute.ConstructorArguments).Value);
        Assert.Contains(
            attribute.NamedArguments,
            argument => argument.Key == "Url" && argument.Value.Value is string actualUrl && actualUrl == url);
    }

    private static void AssertNoPreviewFeatureAttribute(INamedTypeSymbol? packageType)
    {
        Assert.NotNull(packageType);
        Assert.DoesNotContain(
            packageType.GetAttributes(),
            attribute => attribute.AttributeClass?.ToDisplayString() == RequiresPreviewFeaturesAttribute);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDPackagePreviewFeatureTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
