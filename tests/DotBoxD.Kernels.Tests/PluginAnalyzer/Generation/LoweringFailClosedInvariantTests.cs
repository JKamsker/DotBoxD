using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generation;

public sealed class LoweringFailClosedInvariantTests
{
    private static readonly HashSet<string> AllowedNullSwitchFallbackMethods = new(StringComparer.Ordinal)
    {
        "InitializerName",
        "LowerDefault",
        "SandboxTypeExpressionShape",
        "SandboxTypeInvocationShape",
        "SandboxTypeMemberShape",
    };

    [Fact]
    public void Lowering_diagnostic_catalog_entries_are_documented_DBXK_rules()
    {
        var entries = LoweringDiagnosticCatalog.Entries;
        Assert.NotEmpty(entries);

        var duplicateKeys = entries
            .GroupBy(entry => entry.Surface + "|" + entry.Descriptor.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicateKeys);

        var releaseText = AnalyzerReleaseText();
        foreach (var entry in entries)
        {
            Assert.StartsWith("DBXK", entry.Descriptor.Id, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(entry.Surface));
            Assert.False(string.IsNullOrWhiteSpace(entry.FactoryTypeName));
            Assert.False(string.IsNullOrWhiteSpace(entry.FailureRoute));
            Assert.False(string.IsNullOrWhiteSpace(entry.UnsupportedShapeFamily));
            Assert.Contains(entry.Descriptor.Id, releaseText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NotSupported_lowering_factory_catches_are_cataloged_and_route_to_diagnostics()
    {
        var catalogedFactories = LoweringDiagnosticCatalog.Entries
            .Select(entry => entry.FactoryTypeName)
            .Where(factory => !string.Equals(factory, "GeneratorGuard", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var catches = NotSupportedFactoryCatches().ToArray();
        var uncataloged = catches
            .Select(item => item.FactoryTypeName)
            .Distinct(StringComparer.Ordinal)
            .Where(factory => !catalogedFactories.Contains(factory))
            .ToArray();

        Assert.Empty(uncataloged);
        foreach (var item in catches)
        {
            if (string.Equals(item.FactoryTypeName, "HookChainModelFactory", StringComparison.Ordinal))
            {
                Assert.Contains("NotLoweredDiagnostic", item.MethodText, StringComparison.Ordinal);
                continue;
            }

            Assert.True(
                item.CatchText.Contains("Fail(", StringComparison.Ordinal) ||
                item.CatchText.Contains("PluginKernelDiagnostic", StringComparison.Ordinal),
                $"{item.FactoryTypeName}.{item.MethodName} catches NotSupportedException without routing to a diagnostic.");
        }
    }

    [Fact]
    public void Lowering_switch_fallbacks_do_not_return_null_from_dispatch_methods()
    {
        var offenders = new List<string>();
        foreach (var file in LoweringSourceFiles())
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();
            foreach (var arm in root.DescendantNodes().OfType<SwitchExpressionArmSyntax>())
            {
                if (!IsNullLiteral(arm.Expression))
                {
                    continue;
                }

                var methodName = EnclosingMethodName(arm);
                if (methodName.StartsWith("Try", StringComparison.Ordinal) ||
                    AllowedNullSwitchFallbackMethods.Contains(methodName))
                {
                    continue;
                }

                offenders.Add(FormatLocation(file, tree, arm, methodName));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Lowering switch defaults must throw or call an Unsupported helper; nullable fallthrough is only for Try* recognizers:\n" +
            string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<FactoryCatch> NotSupportedFactoryCatches()
    {
        foreach (var file in Directory.EnumerateFiles(AnalysisRoot(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (!string.Equals(catchClause.Declaration?.Type.ToString(), "NotSupportedException", StringComparison.Ordinal))
                {
                    continue;
                }

                var typeName = catchClause.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
                if (typeName is null || (!typeName.EndsWith("ModelFactory", StringComparison.Ordinal) &&
                                         !string.Equals(typeName, "HookChainModelFactory", StringComparison.Ordinal)))
                {
                    continue;
                }

                var method = catchClause.Ancestors().OfType<MethodDeclarationSyntax>().First();
                if (!IsGeneratorEntryFactory(method.Identifier.ValueText))
                {
                    continue;
                }

                yield return new FactoryCatch(typeName, method.Identifier.ValueText, method.ToString(), catchClause.ToString());
            }
        }
    }

    private static bool IsGeneratorEntryFactory(string methodName)
        => string.Equals(methodName, "Create", StringComparison.Ordinal) ||
           string.Equals(methodName, "CreateTarget", StringComparison.Ordinal) ||
           string.Equals(methodName, "CreateRoot", StringComparison.Ordinal);

    private static IEnumerable<string> LoweringSourceFiles()
    {
        var root = AnalysisRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("Lowering/", StringComparison.Ordinal) ||
                relative.StartsWith("Rpc/Lowering/", StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static string AnalyzerReleaseText()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer");
        return File.ReadAllText(Path.Combine(root, "AnalyzerReleases.Shipped.md")) +
               File.ReadAllText(Path.Combine(root, "AnalyzerReleases.Unshipped.md"));
    }

    private static string AnalysisRoot()
        => Path.Combine(RepositoryRoot(), "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer", "Analysis");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static bool IsNullLiteral(ExpressionSyntax expression)
        => expression.IsKind(SyntaxKind.NullLiteralExpression);

    private static string EnclosingMethodName(SyntaxNode node)
        => node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<unknown>";

    private static string FormatLocation(string file, SyntaxTree tree, SyntaxNode node, string methodName)
    {
        var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var relative = Path.GetRelativePath(RepositoryRoot(), file).Replace(Path.DirectorySeparatorChar, '/');
        return $"{relative}:{line} in {methodName}";
    }

    private sealed record FactoryCatch(
        string FactoryTypeName,
        string MethodName,
        string MethodText,
        string CatchText);
}
