using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodProjectionRegressionTests
{
    [Fact]
    public async Task KernelMethod_record_argument_after_Select_uses_projected_record_value()
    {
        var assembly = Compile(
            PluginAnalyzerKernelMethodTestSources.ProjectedRecordHelperUsesProjectedValueChain,
            enableInterceptors: true);
        var package = HookChainPackage(assembly);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", -7, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-2", message.TargetId);
    }

    private static PluginPackage HookChainPackage(Assembly assembly)
    {
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDKernelMethodProjectionRegressionTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(KernelMethodAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK100");
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Id == "DBXK100");
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static SandboxPolicy SandboxedPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
