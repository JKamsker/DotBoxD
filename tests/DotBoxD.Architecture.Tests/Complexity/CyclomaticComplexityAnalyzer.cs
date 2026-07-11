using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Architecture.Tests;

internal sealed record ComplexityBlock(
    string File,
    string Kind,
    string Name,
    int StartLine,
    int EndLine,
    int Complexity);

internal static class CyclomaticComplexityAnalyzer
{
    public static IEnumerable<ComplexityBlock> AnalyzeSourceTree(string root, string sourceRoot)
    {
        foreach (var file in EnumerateSourceFiles(sourceRoot))
        {
            foreach (var block in AnalyzeFile(root, file))
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<ComplexityBlock> AnalyzeFile(string root, string file)
    {
        var source = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            file);
        var rootNode = tree.GetCompilationUnitRoot();
        var parseErrors = rootNode.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (parseErrors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Could not parse {Path.GetRelativePath(root, file)}: {parseErrors[0].GetMessage()}");
        }

        var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
        foreach (var block in ExecutableBlockCollector.Collect(rootNode, relative))
        {
            var span = tree.GetLineSpan(block.Span);
            yield return new ComplexityBlock(
                block.File,
                block.Kind,
                block.Name,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                CyclomaticComplexityWalker.Calculate(block.Nodes));
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".g.cs", StringComparison.Ordinal)
                && !file.Contains($"{separator}Generated{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal));
    }
}

internal sealed record ExecutableBlock(
    string File,
    string Kind,
    string Name,
    TextSpan Span,
    ImmutableArray<SyntaxNode> Nodes);

internal static class ExecutableBlockCollector
{
    public static IReadOnlyList<ExecutableBlock> Collect(CompilationUnitSyntax root, string file)
    {
        List<ExecutableBlock> blocks = [];
        AddTopLevelBlock(root, file, blocks);

        foreach (var node in root.DescendantNodes())
        {
            var block = node switch
            {
                BaseMethodDeclarationSyntax method when HasBody(method) => MethodBlock(file, method),
                AccessorDeclarationSyntax accessor when HasBody(accessor) => AccessorBlock(file, accessor),
                PropertyDeclarationSyntax property when property.ExpressionBody is not null => MemberBlock(file, "property", PropertyName(property), property),
                IndexerDeclarationSyntax indexer when indexer.ExpressionBody is not null => MemberBlock(file, "indexer", QualifiedName(indexer, "this[]"), indexer),
                LocalFunctionStatementSyntax localFunction when HasBody(localFunction) => LocalFunctionBlock(file, localFunction),
                AnonymousFunctionExpressionSyntax anonymousFunction => AnonymousFunctionBlock(file, anonymousFunction),
                _ => null,
            };

            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    private static void AddTopLevelBlock(CompilationUnitSyntax root, string file, ICollection<ExecutableBlock> blocks)
    {
        var statements = root.Members.OfType<GlobalStatementSyntax>().ToArray();
        if (statements.Length == 0)
        {
            return;
        }

        var span = TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);
        blocks.Add(new(file, "top-level", "<top-level statements>", span, [.. statements]));
    }

    private static ExecutableBlock MethodBlock(string file, BaseMethodDeclarationSyntax method)
        => new(file, MethodKind(method), QualifiedName(method, MethodName(method)), method.Span, [method]);

    private static ExecutableBlock AccessorBlock(string file, AccessorDeclarationSyntax accessor)
    {
        var memberName = accessor.Parent?.Parent switch
        {
            PropertyDeclarationSyntax property => PropertyName(property),
            IndexerDeclarationSyntax indexer => QualifiedName(indexer, "this[]"),
            EventDeclarationSyntax eventDeclaration => QualifiedName(eventDeclaration, eventDeclaration.Identifier.ValueText),
            _ => "<unknown>",
        };

        return new(file, "accessor", $"{memberName}.{accessor.Keyword.ValueText}", accessor.Span, [accessor]);
    }

    private static ExecutableBlock MemberBlock(string file, string kind, string name, SyntaxNode member)
        => new(file, kind, name, member.Span, [member]);

    private static ExecutableBlock LocalFunctionBlock(string file, LocalFunctionStatementSyntax localFunction)
        => new(file, "local-function", QualifiedName(localFunction, $"{localFunction.Identifier.ValueText} (local)"), localFunction.Span, [localFunction]);

    private static ExecutableBlock AnonymousFunctionBlock(string file, AnonymousFunctionExpressionSyntax anonymousFunction)
    {
        var owner = anonymousFunction.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() switch
        {
            BaseMethodDeclarationSyntax method => QualifiedName(method, MethodName(method)),
            PropertyDeclarationSyntax property => PropertyName(property),
            IndexerDeclarationSyntax indexer => QualifiedName(indexer, "this[]"),
            _ => "<unknown>",
        };

        var line = anonymousFunction.SyntaxTree.GetLineSpan(anonymousFunction.Span).StartLinePosition.Line + 1;
        return new(file, "lambda", $"{owner}.lambda@{line}", anonymousFunction.Span, [anonymousFunction]);
    }

    private static bool HasBody(BaseMethodDeclarationSyntax method)
        => method.Body is not null || method.ExpressionBody is not null;

    private static bool HasBody(AccessorDeclarationSyntax accessor)
        => accessor.Body is not null || accessor.ExpressionBody is not null;

    private static bool HasBody(LocalFunctionStatementSyntax localFunction)
        => localFunction.Body is not null || localFunction.ExpressionBody is not null;

    private static string MethodKind(BaseMethodDeclarationSyntax method)
        => method switch
        {
            ConstructorDeclarationSyntax => "constructor",
            DestructorDeclarationSyntax => "destructor",
            ConversionOperatorDeclarationSyntax => "conversion-operator",
            OperatorDeclarationSyntax => "operator",
            _ => "method",
        };

    private static string MethodName(BaseMethodDeclarationSyntax method)
        => method switch
        {
            MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
            ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
            DestructorDeclarationSyntax declaration => $"~{declaration.Identifier.ValueText}",
            ConversionOperatorDeclarationSyntax declaration => $"operator {declaration.Type}",
            OperatorDeclarationSyntax declaration => $"operator {declaration.OperatorToken.ValueText}",
            _ => "<unknown>",
        };

    private static string PropertyName(PropertyDeclarationSyntax property)
        => QualifiedName(property, property.Identifier.ValueText);

    private static string QualifiedName(SyntaxNode node, string memberName)
    {
        var containers = node.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(type => type.Identifier.ValueText)
            .Reverse();
        var prefix = string.Join(".", containers);
        return prefix.Length == 0 ? memberName : $"{prefix}.{memberName}";
    }
}
