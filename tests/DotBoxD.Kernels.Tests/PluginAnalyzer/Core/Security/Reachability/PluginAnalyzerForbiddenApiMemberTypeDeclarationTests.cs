using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiMemberTypeDeclarationTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static TheoryData<MemberTypeDeclarationCase> ForbiddenMemberTypeCases { get; } = new()
    {
        new(
            "field type",
            "private System.IO.FileInfo? _info;",
            string.Empty,
            "_info is null"),
        new(
            "property type",
            "public System.IO.FileInfo? Info { get; } = null;",
            string.Empty,
            "Info is null"),
        new(
            "helper return type",
            string.Empty,
            "public static System.IO.FileInfo? CreateInfo() => null;",
            "Helper.CreateInfo() is null"),
        new(
            "helper parameter type",
            string.Empty,
            "public static bool AcceptInfo(System.IO.FileInfo? info) => info is null;",
            "Helper.AcceptInfo(null)"),
    };

    [Theory]
    [MemberData(nameof(ForbiddenMemberTypeCases))]
    public async Task Reports_forbidden_host_type_in_member_declarations(MemberTypeDeclarationCase testCase)
    {
        var diagnostics = await AnalyzeAsync($$"""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    {{testCase.HelperMember}}
                }

                [Plugin("member-type-leak")]
                public sealed class MemberTypeKernel : IEventKernel<string>
                {
                    {{testCase.KernelMember}}

                    public bool ShouldHandle(string e, HookContext context) => {{testCase.ShouldHandleExpression}};

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Matches(@"System\.IO(\.FileInfo)?", diagnostic.GetMessage());
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerForbiddenMemberTypeDeclarationTest",
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

    public sealed record MemberTypeDeclarationCase(
        string Name,
        string KernelMember,
        string HelperMember,
        string ShouldHandleExpression)
    {
        public override string ToString() => Name;
    }
}
