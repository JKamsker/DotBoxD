using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiMemberAttributeMetadataTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static TheoryData<MemberAttributeCase> ForbiddenMemberAttributeCases { get; } = new()
    {
        new(
            "method attribute",
            "[UsesType(typeof(System.IO.FileInfo))] public static bool Accept(string value) => value.Length > 0;"),
        new(
            "return attribute",
            "[return: UsesType(typeof(System.IO.FileInfo))] public static bool Accept(string value) => value.Length > 0;"),
        new(
            "parameter attribute",
            "public static bool Accept([UsesType(typeof(System.IO.FileInfo))] string value) => value.Length > 0;"),
    };

    [Theory]
    [MemberData(nameof(ForbiddenMemberAttributeCases))]
    public async Task Reports_forbidden_host_type_in_ordinary_member_attribute_metadata(
        MemberAttributeCase testCase)
    {
        var diagnostics = await AnalyzeAsync(SourceWithHelper(testCase.HelperMember));

        var diagnostic = Assert.Single(ForbiddenFileInfoDiagnostics(diagnostics));
        Assert.Contains("System.IO.FileInfo", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_benign_ordinary_member_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync(SourceWithHelper(
            "[UsesType(typeof(string))] public static bool Accept(string value) => value.Length > 0;"));

        Assert.Empty(ForbiddenFileInfoDiagnostics(diagnostics));
    }

    private static IEnumerable<Diagnostic> ForbiddenFileInfoDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        => diagnostics.Where(d =>
            d.Id == "DBXK001"
            && d.GetMessage().Contains("System.IO.FileInfo", StringComparison.Ordinal));

    private static string SourceWithHelper(string helperMember)
        => $$"""
            namespace Sample
            {
                using System;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [AttributeUsage(AttributeTargets.Method | AttributeTargets.ReturnValue | AttributeTargets.Parameter)]
                public sealed class UsesTypeAttribute : Attribute
                {
                    public UsesTypeAttribute(Type type) => Type = type;

                    public Type Type { get; }
                }

                public static class Helper
                {
                    {{helperMember}}
                }

                [Plugin("member-attribute-leak")]
                public sealed class MemberAttributeKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => Helper.Accept(e);

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

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
            "DotBoxDPluginAnalyzerForbiddenMemberAttributeMetadataTest",
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

    public sealed record MemberAttributeCase(string Name, string HelperMember)
    {
        public override string ToString() => Name;
    }
}
