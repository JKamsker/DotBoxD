using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrMarkedCallReader
{
    public static MergeableIrMarkedLoweringCall? TryRead(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count != 1 ||
            model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.IsStatic ||
            method.IsExtensionMethod ||
            method.Parameters.Length != 1)
        {
            return null;
        }

        var parameter = method.Parameters[0];
        if (MarkedKind(parameter, model.Compilation) is not { } kind)
        {
            return null;
        }

        if (DelegateTypes(parameter.Type, kind) is not { } types)
        {
            throw new NotSupportedException("the marked parameter must be Func<T, bool> or Func<T, TNext>.");
        }

        return new MergeableIrMarkedLoweringCall(
            method,
            parameter,
            invocation.ArgumentList.Arguments[0],
            kind,
            types.Input,
            types.Output);
    }

    private static MergeableIrLoweredStepKind? MarkedKind(IParameterSymbol parameter, Compilation compilation)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.LowerToIrAttribute) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int value)
            {
                continue;
            }

            return value switch
            {
                0 => MergeableIrLoweredStepKind.Filter,
                1 => MergeableIrLoweredStepKind.Projection,
                _ => throw new NotSupportedException("the [LowerToIr] step kind is not supported.")
            };
        }

        return null;
    }

    private static (ITypeSymbol Input, ITypeSymbol Output)? DelegateTypes(
        ITypeSymbol type,
        MergeableIrLoweredStepKind kind)
    {
        if (type is not INamedTypeSymbol { Name: "Func", ContainingNamespace: { } ns } func ||
            !string.Equals(ns.ToDisplayString(), "System", StringComparison.Ordinal) ||
            func.TypeArguments.Length != 2)
        {
            return null;
        }

        if (kind == MergeableIrLoweredStepKind.Filter &&
            func.TypeArguments[1].SpecialType != SpecialType.System_Boolean)
        {
            throw new NotSupportedException("filter steps must return bool.");
        }

        return (func.TypeArguments[0], func.TypeArguments[1]);
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}
