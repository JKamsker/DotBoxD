using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageIrOutputFactory
{
    private static ITypeSymbol OutputType(
        HookChainStage stage,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var typeInfo = model.GetTypeInfo(body, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        return type is { TypeKind: not TypeKind.Error }
            ? type
            : throw new NotSupportedException("the projection output type could not be resolved.");
    }

    internal static bool TryOutputType(
        HookChainStage stage,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol outputType)
    {
        outputType = null!;
        try
        {
            outputType = OutputType(stage, model, cancellationToken);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    internal static HookChainStageOutputShape OutputShape(
        HookChainStage stage,
        DotBoxDExpressionModel value,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!stage.IsSelect)
        {
            var boolType = model.Compilation.GetSpecialType(SpecialType.System_Boolean);
            return new HookChainStageOutputShape(
                DotBoxDGenerationNames.ManifestTypes.Bool,
                boolType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (TryOutputType(stage, model, cancellationToken, out var outputType))
        {
            return new HookChainStageOutputShape(
                SandboxTypeSourceEmitter.ManifestTag(outputType),
                outputType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        return new HookChainStageOutputShape(
            value.Type,
            GeneratedRemoteHookChainFallback.TypeFullName(body, model, cancellationToken, value.Type));
    }

    internal static (string? ElementParam, string? ContextParam) LambdaParameters(LambdaExpressionSyntax lambda)
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

    internal static ITypeSymbol? LambdaParameterType(
        LambdaExpressionSyntax lambda,
        string parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters })
        {
            foreach (var parameter in parameters)
            {
                if (!string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                var type = (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;
                return type is { TypeKind: not TypeKind.Error }
                    ? type
                    : GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
            }
        }

        return GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
    }

    internal readonly record struct HookChainStageOutputShape(
        string Tag,
        string FullName);
}
