using DotBoxD.Plugins.Analyzer.Analysis.PluginServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFrameworkFacadeAttributeTests
{
    [Fact]
    public void Net461_mscorlib_obsolete_attribute_is_preserved()
    {
        var compilation = CSharpCompilation.Create(
            "Net461FacadeAttributeContract",
            [CSharpSyntaxTree.ParseText("""
                public interface IService
                {
                    [System.Obsolete("Use Ping")]
                    int LegacyPing();
                }
                """)],
            [MetadataReference.CreateFromFile(PackageReference(
                "microsoft.netframework.referenceassemblies.net461",
                "build/.NETFramework/v4.6.1/mscorlib.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("IService")!
            .GetMembers("LegacyPing")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use Ping\")]",
            PluginServerFlowAttributeSource.MemberAttributes(method));
    }

    private static string PackageReference(string packageId, string relativePath)
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            packageId);
        var path = Directory.EnumerateFiles(packageRoot, Path.GetFileName(relativePath), SearchOption.AllDirectories)
            .Where(candidate => candidate.Replace(Path.DirectorySeparatorChar, '/')
                .EndsWith(relativePath, StringComparison.Ordinal))
            .OrderByDescending(static candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        Assert.NotNull(path);
        return path;
    }
}
