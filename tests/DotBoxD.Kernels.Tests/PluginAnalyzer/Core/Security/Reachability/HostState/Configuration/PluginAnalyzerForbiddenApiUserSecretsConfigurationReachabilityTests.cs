using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiUserSecretsConfigurationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "user secrets provider",
        """
        private static readonly IConfiguration Configuration =
            new ConfigurationBuilder()
                .AddUserSecrets<UserSecretsKernel>(optional: true)
                .Build();
        """,
        "Microsoft.Extensions.Configuration.UserSecrets")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-secrets.json\");",
        "System.IO.File")]
    public async Task Reports_forbidden_user_secrets_configuration_in_static_initializers(
        string testCase,
        string staticMember,
        string expectedForbiddenApi)
    {
        var source = Source(staticMember);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedForbiddenApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMember)
        => $$"""
            #nullable enable

            [assembly: Microsoft.Extensions.Configuration.UserSecrets.UserSecretsId("probe")]

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Configuration;

                [Plugin("user-secrets-host-api")]
                public sealed class UserSecretsKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => e.Length >= 0;

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
            "DotBoxDPluginAnalyzerUserSecretsConfigurationReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(ConfigurationReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> ConfigurationReferences()
    {
        yield return MetadataReference.CreateFromFile(
            FindAspNetCoreSharedAssembly("Microsoft.Extensions.Configuration.Abstractions.dll"));
        yield return MetadataReference.CreateFromFile(
            FindAspNetCoreSharedAssembly("Microsoft.Extensions.Configuration.dll"));
        yield return MetadataReference.CreateFromFile(
            FindAspNetCoreSharedAssembly("Microsoft.Extensions.Configuration.UserSecrets.dll"));
    }

    private static string FindAspNetCoreSharedAssembly(string fileName)
    {
        var trustedPlatformAssembly = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(reference => string.Equals(Path.GetFileName(reference), fileName, StringComparison.Ordinal));
        if (trustedPlatformAssembly is not null)
        {
            return trustedPlatformAssembly;
        }

        var runtimeDirectory = Directory.GetParent(typeof(object).Assembly.Location)!.FullName;
        var sharedRoot = Directory.GetParent(Directory.GetParent(runtimeDirectory)!.FullName)!.FullName;
        var frameworkRoot = Path.Combine(sharedRoot, "Microsoft.AspNetCore.App");
        var assembly = Directory.EnumerateDirectories(frameworkRoot)
            .OrderByDescending(ParseFrameworkDirectoryVersion)
            .Select(directory => Path.Combine(directory, fileName))
            .First(File.Exists);

        return assembly;
    }

    private static Version ParseFrameworkDirectoryVersion(string directory)
    {
        var directoryName = Path.GetFileName(directory);
        var suffixStart = directoryName.IndexOf('-');
        if (suffixStart >= 0)
        {
            directoryName = directoryName[..suffixStart];
        }

        return Version.TryParse(directoryName, out var version) ? version : new Version();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
