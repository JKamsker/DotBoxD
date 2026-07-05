using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static readonly DeclaredTypeExpressionResolver[] DeclaredTypeExpressionResolvers =
    [
        TryDeclaredCastType,
        TryDeclaredAsType,
        TryDeclaredAwaitedType,
        TryDeclaredTaskResultType,
    ];

    private static readonly DeclaredTypeSymbolResolver[] DeclaredTypeSymbolResolvers =
    [
        TryParameterTypeSyntax,
        TryLocalTypeSyntax,
        TryFieldTypeSyntax,
        TryPropertyTypeSyntax,
        TryMethodReturnTypeSyntax,
    ];

    private static TypeSyntax? TryDeclaredCastType(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => expression is CastExpressionSyntax { Type: { } castType } ? castType : null;

    private static TypeSyntax? TryDeclaredAsType(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => expression is BinaryExpressionSyntax asExpression &&
           asExpression.IsKind(SyntaxKind.AsExpression) &&
           asExpression.Right is TypeSyntax asType
            ? asType
            : null;

    private static TypeSyntax? TryDeclaredAwaitedType(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => expression is AwaitExpressionSyntax awaitExpression
            ? AwaitedTypeSyntax(awaitExpression, model, cancellationToken)
            : null;

    private static TypeSyntax? TryDeclaredTaskResultType(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } resultAccess
            ? TaskResultTypeSyntax(resultAccess, model, cancellationToken)
            : null;

    private static TypeSyntax? TryParameterTypeSyntax(ISymbol? symbol, SemanticModel model, CancellationToken cancellationToken)
        => symbol is IParameterSymbol parameter ? ParameterTypeSyntax(parameter, cancellationToken) : null;

    private static TypeSyntax? TryLocalTypeSyntax(ISymbol? symbol, SemanticModel model, CancellationToken cancellationToken)
        => symbol is ILocalSymbol local ? LocalTypeSyntax(local, model, cancellationToken) : null;

    private static TypeSyntax? TryFieldTypeSyntax(ISymbol? symbol, SemanticModel model, CancellationToken cancellationToken)
        => symbol is IFieldSymbol field ? FieldTypeSyntax(field, cancellationToken) : null;

    private static TypeSyntax? TryPropertyTypeSyntax(ISymbol? symbol, SemanticModel model, CancellationToken cancellationToken)
        => symbol is IPropertySymbol property ? PropertyTypeSyntax(property, cancellationToken) : null;

    private static TypeSyntax? TryMethodReturnTypeSyntax(ISymbol? symbol, SemanticModel model, CancellationToken cancellationToken)
        => symbol is IMethodSymbol method ? MethodReturnTypeSyntax(method, cancellationToken) : null;

    private delegate TypeSyntax? DeclaredTypeExpressionResolver(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken);

    private delegate TypeSyntax? DeclaredTypeSymbolResolver(
        ISymbol? symbol,
        SemanticModel model,
        CancellationToken cancellationToken);
}
