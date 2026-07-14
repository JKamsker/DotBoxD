using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiReviewRegressionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_ValueTask_payload_in_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("value-task-payload")]
                public sealed class ValueTaskPayloadKernel : IEventKernel<string>
                {
                    private static readonly ValueTask<System.IO.FileInfo> Leaked =
                        new(new System.IO.FileInfo("/etc/passwd"));

                    public bool ShouldHandle(string e, HookContext context) => Leaked.IsCompleted;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_module_initializer_when_kernel_is_nested()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System.Runtime.CompilerServices;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public static class Container
                {
                    [Plugin("nested-kernel")]
                    public sealed class NestedKernel : IEventKernel<string>
                    {
                        public bool ShouldHandle(string e, HookContext context) => true;

                        public void Handle(string e, HookContext context) { }
                    }
                }

                public static class Initializer
                {
                    [ModuleInitializer]
                    public static void Initialize() => System.IO.File.ReadAllText("/etc/passwd");
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Theory]
    [InlineData("_ = default(System.IO.FileInfo);")]
    [InlineData("Use<System.IO.FileInfo>();")]
    [InlineData("System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(\"en-US\");")]
    public async Task Reports_forbidden_module_initializer_syntax_and_mutation(string statement)
    {
        var source = """
            namespace Sample
            {
                using System.Runtime.CompilerServices;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("module-root")]
                public sealed class ModuleRootKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }

                public static class Initializer
                {
                    [ModuleInitializer]
                    public static void Initialize()
                    {
                        STATEMENT
                    }

                    private static void Use<T>() { }
                }
            }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);

        var diagnostics = await AnalyzeAsync(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Theory]
    [InlineData("((dynamic)new Derived()).DangerMethod()")]
    [InlineData("((dynamic)new Derived()).DangerProperty")]
    [InlineData("((dynamic)new Derived())[0]")]
    public async Task Reports_forbidden_dynamic_member_inherited_from_base_type(string access)
    {
        var source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public class Base
                {
                    public int DangerMethod() => System.IO.File.ReadAllText("/etc/passwd").Length;

                    public int DangerProperty => System.IO.File.ReadAllText("/etc/passwd").Length;

                    public int this[int index] => System.IO.File.ReadAllText("/etc/passwd").Length;
                }

                public sealed class Derived : Base { }

                [Plugin("dynamic-inheritance")]
                public sealed class DynamicKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => ACCESS > 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """.Replace("ACCESS", access, StringComparison.Ordinal);

        var diagnostics = await AnalyzeAsync(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_dynamic_property_setter()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public int Danger
                    {
                        get => 0;
                        set => System.IO.File.WriteAllText("/tmp/value", value.ToString());
                    }
                }

                [Plugin("dynamic-setter")]
                public sealed class DynamicSetterKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        ((dynamic)new Helper()).Danger = 1;
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_signature_on_delayed_dynamic_property_target()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public System.IO.FileInfo Info => null!;
                }

                [Plugin("dynamic-signature")]
                public sealed class DynamicSignatureKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        dynamic helper = new Helper();
                        return helper.Info is not null;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerReviewRegressionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
