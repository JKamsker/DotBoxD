using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackagePlatformCompatibilityAttributeSurpriseTests
{
    private const string SupportedOSPlatformAttribute = "System.Runtime.Versioning.SupportedOSPlatformAttribute";
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_package_preserves_platform_compatibility_kernel_contract()
    {
        var compilation = CreateCompilation("""
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [SupportedOSPlatform("windows")]
            [Plugin("platform-damage")]
            public sealed partial class PlatformDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext context) => true;

                public void Handle(DamageEvent e, HookContext context)
                    => context.Messages.Send(e.TargetId, "platform damage");
            }

            [Plugin("portable-damage")]
            public sealed partial class PortableDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext context) => true;

                public void Handle(DamageEvent e, HookContext context)
                    => context.Messages.Send(e.TargetId, "portable damage");
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

        var platformPackage = outputCompilation.GetTypeByMetadataName("Sample.PlatformDamagePluginPackage");
        var portablePackage = outputCompilation.GetTypeByMetadataName("Sample.PortableDamagePluginPackage");

        Assert.NotNull(platformPackage);
        Assert.NotNull(portablePackage);
        Assert.Contains(platformPackage.GetAttributes(), IsWindowsSupportedPlatformAttribute);
        Assert.DoesNotContain(portablePackage.GetAttributes(), IsSupportedPlatformAttribute);
    }

    private static bool IsWindowsSupportedPlatformAttribute(AttributeData attribute)
        => IsSupportedPlatformAttribute(attribute) &&
           attribute.ConstructorArguments is [{ Value: "windows" }];

    private static bool IsSupportedPlatformAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == SupportedOSPlatformAttribute;

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDPlatformPackageTest",
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
