using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiAnonymousFunctionAttributeTypeReferenceTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static TheoryData<AnonymousFunctionAttributeCase> ForbiddenAttributeCases { get; } = new()
    {
        new(
            "lambda attribute",
            """
            Func<bool> predicate = [UsesType(typeof(System.IO.FileInfo))] () => true;
            return predicate();
            """),
        new(
            "lambda parameter attribute",
            """
            return Helper.Accept(([UsesType(typeof(System.IO.FileInfo))] string value) => value.Length > 0);
            """),
    };

    [Theory]
    [MemberData(nameof(ForbiddenAttributeCases))]
    public async Task Reports_forbidden_host_type_in_anonymous_function_attribute_metadata(
        AnonymousFunctionAttributeCase testCase)
    {
        var diagnostics = await AnalyzeAsync(KernelSource(testCase.ShouldHandleBody));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("System.IO.FileInfo", StringComparison.Ordinal)
            || message.Contains("System.IO", StringComparison.Ordinal),
            message);
    }

    [Fact]
    public async Task Does_not_report_benign_anonymous_function_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync(KernelSource("""
            Func<bool> predicate = [UsesType(typeof(string))] () => true;
            return predicate();
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string KernelSource(string shouldHandleBody) => $$"""
        #nullable enable

        namespace Sample
        {
            using System;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class UsesTypeAttribute : Attribute
            {
                public UsesTypeAttribute(Type type)
                {
                    Type = type;
                }

                public Type Type { get; }
            }

            public static class Helper
            {
                public static bool Accept(Func<string, bool> predicate) => predicate("ok");
            }

            [Plugin("anonymous-function-attribute-leak")]
            public sealed class AnonymousFunctionAttributeKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{shouldHandleBody}}
                }

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
            "DotBoxDPluginAnalyzerForbiddenAnonymousFunctionAttributeTypeReferenceTest",
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

    public sealed record AnonymousFunctionAttributeCase(string Name, string ShouldHandleBody)
    {
        public override string ToString() => Name;
    }
}
