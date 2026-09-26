using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackageCodeRequirementAttributeSurpriseTests
{
    private const string RequiresDynamicCodeAttribute =
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string RequiresUnreferencedCodeAttribute =
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_packages_preserve_code_requirement_kernel_contracts()
    {
        var compilation = CreateCompilation("""
            using System.Diagnostics.CodeAnalysis;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [RequiresUnreferencedCode("Trimming the plugin package requires preserved metadata.", Url = "https://example.test/trimming")]
            [Plugin("sample.trimming")]
            public sealed partial class TrimmingDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "trimming");
            }

            [RequiresDynamicCode("The plugin package requires runtime code generation.", Url = "https://example.test/dynamic-code")]
            [Plugin("sample.dynamic-code")]
            public sealed partial class DynamicCodeDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "dynamic code");
            }

            [Plugin("sample.portable")]
            public sealed partial class PortableDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "portable");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(3, PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()).GeneratedTrees.Length);

        AssertCodeRequirementAttribute(
            outputCompilation.GetTypeByMetadataName("Sample.TrimmingDamagePluginPackage"),
            RequiresUnreferencedCodeAttribute,
            "Trimming the plugin package requires preserved metadata.",
            "https://example.test/trimming");
        AssertCodeRequirementAttribute(
            outputCompilation.GetTypeByMetadataName("Sample.DynamicCodeDamagePluginPackage"),
            RequiresDynamicCodeAttribute,
            "The plugin package requires runtime code generation.",
            "https://example.test/dynamic-code");
        AssertNoCodeRequirementAttributes(
            outputCompilation.GetTypeByMetadataName("Sample.PortableDamagePluginPackage"));
    }

    private static void AssertCodeRequirementAttribute(
        INamedTypeSymbol? packageType,
        string attributeMetadataName,
        string message,
        string url)
    {
        Assert.NotNull(packageType);
        var attribute = Assert.Single(
            packageType.GetAttributes(),
            candidate => candidate.AttributeClass?.ToDisplayString() == attributeMetadataName);

        Assert.Equal(message, Assert.Single(attribute.ConstructorArguments).Value);
        Assert.Contains(
            attribute.NamedArguments,
            argument => argument.Key == "Url" && argument.Value.Value is string actualUrl && actualUrl == url);
    }

    private static void AssertNoCodeRequirementAttributes(INamedTypeSymbol? packageType)
    {
        Assert.NotNull(packageType);
        var attributes = packageType.GetAttributes();

        Assert.DoesNotContain(
            attributes,
            attribute => attribute.AttributeClass?.ToDisplayString() is RequiresUnreferencedCodeAttribute or RequiresDynamicCodeAttribute);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDPackageCodeRequirementTest",
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
