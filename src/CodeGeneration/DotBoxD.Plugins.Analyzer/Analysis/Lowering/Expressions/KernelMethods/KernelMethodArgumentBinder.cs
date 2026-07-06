using System.Collections.Immutable;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodArgumentBinder
{
    public static BoundKernelMethodCall Bind(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        Compilation compilation,
        string description)
    {
        var definition = method.ReducedFrom ?? method;
        ValidateMethod(definition, description);

        var parameters = definition.Parameters;
        var bound = new BoundKernelMethodArgument[parameters.Length];
        var evaluationOrder = new List<BoundKernelMethodArgument>(parameters.Length);
        var assigned = new bool[parameters.Length];
        var nextPositional = 0;
        BindExtensionReceiver(invocation, method, parameters, bound, evaluationOrder, assigned, description, ref nextPositional);
        BindExplicitArguments(invocation, parameters, bound, evaluationOrder, assigned, description, ref nextPositional);
        BindDefaultArguments(parameters, bound, assigned, description);

        return new BoundKernelMethodCall(definition, bound, evaluationOrder);
    }

    public static IMethodSymbol Definition(IMethodSymbol method)
        => method.ReducedFrom ?? method;

    private static void BindExtensionReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ImmutableArray<IParameterSymbol> parameters,
        BoundKernelMethodArgument[] bound,
        List<BoundKernelMethodArgument> evaluationOrder,
        bool[] assigned,
        string description,
        ref int nextPositional)
    {
        if (method.ReducedFrom is null)
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax extensionAccess)
        {
            throw new NotSupportedException($"{description} extension call has no receiver expression.");
        }

        bound[0] = new BoundKernelMethodArgument(parameters[0], extensionAccess.Expression, null, UsesDefault: false);
        evaluationOrder.Add(bound[0]);
        assigned[0] = true;
        nextPositional = 1;
    }

    private static void BindExplicitArguments(
        InvocationExpressionSyntax invocation,
        ImmutableArray<IParameterSymbol> parameters,
        BoundKernelMethodArgument[] bound,
        List<BoundKernelMethodArgument> evaluationOrder,
        bool[] assigned,
        string description,
        ref int nextPositional)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            BindExplicitArgument(argument, parameters, bound, evaluationOrder, assigned, description, ref nextPositional);
        }
    }

    private static void BindExplicitArgument(
        ArgumentSyntax argument,
        ImmutableArray<IParameterSymbol> parameters,
        BoundKernelMethodArgument[] bound,
        List<BoundKernelMethodArgument> evaluationOrder,
        bool[] assigned,
        string description,
        ref int nextPositional)
    {
        if (!argument.RefKindKeyword.IsKind(SyntaxKind.None))
        {
            throw new NotSupportedException($"{description} call cannot use ref, in, or out arguments.");
        }

        var index = argument.NameColon is { } name
            ? IndexOfParameter(parameters, name.Name.Identifier.ValueText, description)
            : nextPositional;
        if (index >= parameters.Length || assigned[index])
        {
            throw new NotSupportedException($"{description} call has duplicate or misplaced arguments.");
        }

        bound[index] = new BoundKernelMethodArgument(parameters[index], argument.Expression, null, UsesDefault: false);
        evaluationOrder.Add(bound[index]);
        assigned[index] = true;
        nextPositional = NextUnassigned(assigned, nextPositional);
    }

    private static void BindDefaultArguments(
        ImmutableArray<IParameterSymbol> parameters,
        BoundKernelMethodArgument[] bound,
        bool[] assigned,
        string description)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (assigned[i])
            {
                continue;
            }

            if (!ParameterDefaultValueEmitter.TryGetRuntimeDefaultValue(parameters[i], out var defaultValue))
            {
                throw new NotSupportedException($"{description} call must pass parameter '{parameters[i].Name}'.");
            }

            bound[i] = new BoundKernelMethodArgument(parameters[i], null, defaultValue, UsesDefault: true);
        }
    }

    private static void ValidateMethod(IMethodSymbol method, string description)
    {
        if (method.IsGenericMethod)
        {
            throw new NotSupportedException($"{description} must be non-generic.");
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException($"{description} parameters must be value parameters.");
            }

            if (parameter.IsParams)
            {
                throw new NotSupportedException($"{description} cannot use params parameters.");
            }
        }
    }

    private static int NextUnassigned(bool[] assigned, int start)
    {
        while (start < assigned.Length && assigned[start])
        {
            start++;
        }

        return start;
    }

    private static int IndexOfParameter(IReadOnlyList<IParameterSymbol> parameters, string name, string description)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new NotSupportedException($"{description} has no parameter '{name}'.");
    }

}

internal sealed record BoundKernelMethodCall(
    IMethodSymbol Method,
    IReadOnlyList<BoundKernelMethodArgument> Arguments,
    IReadOnlyList<BoundKernelMethodArgument> EvaluationOrder);

internal sealed record BoundKernelMethodArgument(
    IParameterSymbol Parameter,
    ExpressionSyntax? Expression,
    object? DefaultValue,
    bool UsesDefault);
