using System.Collections.Immutable;
using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class GeneratedPackagePluginIdValidationSurpriseTests
{
    private const string MalformedPluginId = "../bad";

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_package_fails_closed_for_malformed_plugin_id()
    {
        var compilation = CreateCompilation("""
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("../bad")]
            public sealed partial class BadPluginKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);

        AssertGeneratedPackageDoesNotCreateMalformedManifest(compilation);
    }

    [Fact]
    public void Generated_server_extension_package_fails_closed_for_malformed_plugin_id()
    {
        var compilation = CreateCompilation("""
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("../bad")]
            public sealed partial class BadServerExtensionKernel
            {
                public int Ping(HookContext ctx) => 1;
            }
            """);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.True(
            TryAssertFailClosed(generatorDiagnostics, driver.GetRunResult()),
            "Expected a focused DBXK generator diagnostic for the malformed server-extension plugin id.");
    }

    private static void AssertGeneratedPackageDoesNotCreateMalformedManifest(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        if (TryAssertFailClosed(generatorDiagnostics, driver.GetRunResult()))
        {
            return;
        }

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var package = InvokeGeneratedPackageFactory(outputCompilation);

        Assert.NotEqual(MalformedPluginId, package.Manifest.PluginId);
    }

    private static bool TryAssertFailClosed(
        ImmutableArray<Diagnostic> generatorDiagnostics,
        GeneratorDriverRunResult runResult)
    {
        var failClosed = generatorDiagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("plugin id", StringComparison.OrdinalIgnoreCase) &&
                          diagnostic.GetMessage().Contains("identifier", StringComparison.OrdinalIgnoreCase));
        if (failClosed is null)
        {
            return false;
        }

        Assert.Equal(DiagnosticSeverity.Error, failClosed.Severity);
        Assert.Empty(runResult.GeneratedTrees);
        return true;
    }

    private static PluginPackage InvokeGeneratedPackageFactory(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var assembly = Assembly.Load(stream.ToArray());
        var packageType = Assert.Single(assembly.GetTypes(), type =>
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static) is not null);
        var package = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

        return Assert.IsType<PluginPackage>(package);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDGeneratedPackageIdValidationTest",
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
