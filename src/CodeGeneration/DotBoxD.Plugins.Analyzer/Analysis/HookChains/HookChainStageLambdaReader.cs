using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageLambdaReader
{
    public static (string? Element, string? Context) Parameters(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, null),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters } => parameters.Count switch
            {
                1 => (parameters[0].Identifier.ValueText, null),
                2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                _ => (null, null),
            },
            _ => (null, null)
        };

    public static ITypeSymbol? ContextType(
        LambdaExpressionSyntax lambda,
        string? parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (parameterName is null)
        {
            return null;
        }

        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters })
        {
            foreach (var parameter in parameters)
            {
                if (string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                {
                    var type = (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;
                    return type is { TypeKind: not TypeKind.Error }
                        ? type
                        : GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
                }
            }
        }

        return GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
    }
}

internal static class HookChainExpressionLoweringContextFactory
{
    public static DotBoxDExpressionLoweringContext Create(
        string elementParameterName,
        string? contextParameterName,
        ITypeSymbol? contextType,
        EquatableArray<EventPropertyModel> eventProperties,
        DotBoxDExpressionModel? projectedElement,
        ITypeSymbol? projectedElementType,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => new(
            elementParameterName,
            eventProperties,
            default,
            model,
            cancellationToken,
            projectedElementName: projectedElement is null ? null : elementParameterName,
            projectedElement,
            projectedElementType: projectedElement is null ? null : projectedElementType,
            rootElementType: eventType,
            serverContextParameterName: contextParameterName,
            serverContextType: contextType,
            capabilities: capabilities,
            effects: effects);
}
