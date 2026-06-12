using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_unsupported_live_setting_type()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                [LiveSetting]
                public decimal Anything { get; set; } = 1m;

                public bool ShouldHandle(string e, HookContext context) => true;

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP020");
    }

    [Fact]
    public async Task Reports_file_io_inside_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    System.IO.File.WriteAllText("x.txt", "bad");
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Theory]
    [InlineData("new System.Net.Http.HttpClient();")]
    [InlineData("System.Diagnostics.Process.Start(\"cmd.exe\");")]
    [InlineData("System.Threading.Tasks.Task.Run(() => { });")]
    [InlineData("System.Threading.Thread.Sleep(1);")]
    [InlineData("System.Environment.GetEnvironmentVariable(\"SECRET\");")]
    [InlineData("((System.IServiceProvider)null!).GetService(typeof(string));")]
    [InlineData("System.IO.Stream.Synchronized(null!);")]
    [InlineData("System.Reflection.Assembly.Load(\"System.Private.CoreLib\");")]
    public async Task Reports_forbidden_host_apis_inside_event_kernel(string statement)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{statement}}
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Fact]
    public void Generates_fire_damage_plugin_package_from_kernel_class()
    {
        var compilation = CreateCompilation("""
            using System.ComponentModel.DataAnnotations;
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string DamageType, int Amount, string TargetId);

            [GamePlugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public string DamageType { get; set; } = "fire";

                [LiveSetting]
                [Range(0, 10_000)]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.DamageType == DamageType &&
                       e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "Ouch, fire.");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var generatedTree = Assert.Single(driver.GetRunResult().GeneratedTrees);
        var generated = generatedTree.GetText().ToString();

        Assert.Contains("public static class FireDamagePluginPackage", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"DamageType\", \"string\", \"fire\")", generated);
        Assert.Contains("new global::SafeIR.Plugins.LiveSettingDefinition(\"MinDamage\", \"int\", 100, 0, 10000)", generated);
        Assert.Contains("And(Eq(Var(\"e_DamageType\"), Var(\"DamageType\")), Ge(Var(\"e_Amount\"), Var(\"MinDamage\")))", generated);
        Assert.Contains("global::SafeIR.Plugins.PluginMessageBindings.SendBindingId", generated);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SafeIrPluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions);
        return CSharpCompilation.Create(
            "SafeIrPluginAnalyzerTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
