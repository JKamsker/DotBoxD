using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiLocalFunctionAttributeTypeReferenceTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "local function attribute",
        "[UsesType(typeof(System.IO.FileInfo))] static bool Local() => true;",
        "return Local();")]
    [InlineData(
        "local function return attribute",
        "[return: UsesType(typeof(System.IO.FileInfo))] static bool Local() => true;",
        "return Local();")]
    [InlineData(
        "local function parameter attribute",
        "static bool Local([UsesType(typeof(System.IO.FileInfo))] string value) => value.Length > 0;",
        "return Local(e);")]
    public async Task Reports_forbidden_host_type_in_local_function_attribute_metadata(
        string testCase,
        string localFunctionDeclaration,
        string returnStatement)
    {
        var source = CreateSource(localFunctionDeclaration, returnStatement);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("System.IO.FileInfo", StringComparison.Ordinal)
            || message.Contains("System.IO", StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Does_not_report_benign_local_function_attribute_metadata()
    {
        var source = CreateSource(
            "[UsesType(typeof(string))] static bool Local() => true;",
            "return Local();");

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string CreateSource(string localFunctionDeclaration, string returnStatement)
    {
        return $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [System.AttributeUsage(
                    System.AttributeTargets.Method
                    | System.AttributeTargets.Parameter
                    | System.AttributeTargets.ReturnValue)]
                public sealed class UsesTypeAttribute : System.Attribute
                {
                    public UsesTypeAttribute(System.Type type)
                    {
                        Type = type;
                    }

                    public System.Type Type { get; }
                }

                [Plugin("local-function-attribute-type-reference")]
                public sealed class LocalFunctionAttributeKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{localFunctionDeclaration}}
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerLocalFunctionAttributeTypeReferenceTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
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
