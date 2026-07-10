using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Debugging;

internal sealed record KernelSourceLocationModel(
    string DocumentId,
    string Path,
    string Sha256Checksum,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public EquatableArray<KernelSourceLocationModel> SequencePoints { get; init; }

    public static PluginKernelModel ApplyToNamedKernel(
        PluginKernelModel model,
        string pluginId,
        MethodDeclarationSyntax shouldHandle,
        MethodDeclarationSyntax handle,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => model with
        {
            ShouldHandleSource = CreateWithKernelMethods(
                pluginId + ":ShouldHandle", shouldHandle, semanticModel, cancellationToken),
            HandleSource = CreateWithKernelMethods(
                pluginId + ":Handle", handle, semanticModel, cancellationToken)
        };

    public static KernelSourceLocationModel Create(
        string documentId,
        SyntaxNode node,
        CancellationToken cancellationToken)
        => CreateComposite(documentId, node, [node], cancellationToken);

    public static KernelSourceLocationModel CreateComposite(
        string documentId,
        SyntaxNode primary,
        IEnumerable<SyntaxNode> nodes,
        CancellationToken cancellationToken)
    {
        var trees = new Dictionary<SyntaxTree, string>();
        trees[primary.SyntaxTree] = documentId;
        var points = new List<KernelSourceLocationModel>();
        var seen = new HashSet<(SyntaxTree Tree, int Start, int Length)>();
        foreach (var candidate in new[] { primary }.Concat(nodes).SelectMany(MeaningfulNodes))
        {
            var key = (candidate.SyntaxTree, candidate.SpanStart, candidate.Span.Length);
            if (!seen.Add(key))
            {
                continue;
            }

            if (!trees.TryGetValue(candidate.SyntaxTree, out var pointDocumentId))
            {
                pointDocumentId = documentId + ":source:" + trees.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                trees.Add(candidate.SyntaxTree, pointDocumentId);
            }

            points.Add(CreateSingle(pointDocumentId, candidate, cancellationToken));
        }

        var result = CreateSingle(documentId, primary, cancellationToken);
        return result with { SequencePoints = new EquatableArray<KernelSourceLocationModel>(points) };
    }

    public static KernelSourceLocationModel CreateWithKernelMethods(
        string documentId,
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => CreateCompositeWithKernelMethods(documentId, node, [node], semanticModel, cancellationToken);

    public static KernelSourceLocationModel CreateCompositeWithKernelMethods(
        string documentId,
        SyntaxNode primary,
        IEnumerable<SyntaxNode> nodes,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sources = nodes.ToList();
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        for (var index = 0; index < sources.Count; index++)
        {
            foreach (var invocation in sources[index].DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var model = semanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (method is null || !IsKernelMethod(method) || !visited.Add(method.OriginalDefinition))
                {
                    continue;
                }

                foreach (var reference in method.DeclaringSyntaxReferences)
                {
                    sources.Add(reference.GetSyntax(cancellationToken));
                }
            }
        }

        return CreateComposite(documentId, primary, sources, cancellationToken);
    }

    private static KernelSourceLocationModel CreateSingle(
        string documentId,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var mapped = node.GetLocation().GetMappedLineSpan();
        var path = string.IsNullOrWhiteSpace(mapped.Path)
            ? node.SyntaxTree.FilePath
            : mapped.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = documentId + ".cs";
        }

        var source = node.SyntaxTree.GetText(cancellationToken).ToString();
        return new KernelSourceLocationModel(
            documentId,
            path,
            Checksum(source),
            mapped.StartLinePosition.Line + 1,
            mapped.StartLinePosition.Character + 1,
            mapped.EndLinePosition.Line + 1,
            mapped.EndLinePosition.Character + 1);
    }

    private static IEnumerable<SyntaxNode> MeaningfulNodes(SyntaxNode node)
        => node.DescendantNodesAndSelf().Where(candidate =>
            candidate is ExpressionSyntax || candidate is StatementSyntax and not BlockSyntax);

    private static bool IsKernelMethod(IMethodSymbol method)
        => method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name == "KernelMethodAttribute" &&
            attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "DotBoxD.Abstractions");

    private static string Checksum(string source)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(source));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
