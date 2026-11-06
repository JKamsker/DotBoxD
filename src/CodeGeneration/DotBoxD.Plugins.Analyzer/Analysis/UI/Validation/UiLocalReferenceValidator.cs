using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

/// <summary>Rejects CLR lifetime and mutable alias semantics that the value-based kernel IR cannot preserve.</summary>
internal static class UiLocalReferenceValidator
{
    public static void Validate(MethodDeclarationSyntax syntax, SemanticModel model, CancellationToken token)
    {
        if (syntax.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Any(local => !local.UsingKeyword.IsKind(SyntaxKind.None)))
        { throw new NotSupportedException("using declarations require CLR disposal and cannot execute as local kernels"); }

        RpcLocalCollectionAliasValidator.Validate(syntax, model, token, valueBoundary: false);
    }
}
