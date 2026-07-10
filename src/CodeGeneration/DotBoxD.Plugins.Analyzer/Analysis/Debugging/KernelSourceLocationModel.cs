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
    public static PluginKernelModel ApplyToNamedKernel(
        PluginKernelModel model,
        string pluginId,
        MethodDeclarationSyntax shouldHandle,
        MethodDeclarationSyntax handle,
        CancellationToken cancellationToken)
        => model with
        {
            ShouldHandleSource = Create(pluginId + ":ShouldHandle", shouldHandle, cancellationToken),
            HandleSource = Create(pluginId + ":Handle", handle, cancellationToken)
        };

    public static KernelSourceLocationModel Create(
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
