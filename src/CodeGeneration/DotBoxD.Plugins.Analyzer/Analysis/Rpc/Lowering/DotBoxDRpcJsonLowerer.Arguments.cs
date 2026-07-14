using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string[] LowerArgumentsInParameterOrder(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string description)
    {
        var bound = BindArgumentsInParameterOrder(arguments, parameters, description);

        var lowered = new string?[parameters.Count];
        foreach (var argument in bound.EvaluationOrder)
        {
            var parameter = parameters[argument.ParameterIndex];
            var value = LowerRequiredExpression(
                argument.Expression,
                parameter.Type,
                $"{description} parameter '{parameter.Name}'");
            if (_expressionPrelude is null)
            {
                lowered[argument.ParameterIndex] = value;
                continue;
            }

            var localName = ReserveGeneratedLocal("__sir_arg");
            AddExpressionPrelude(SetStatement(localName, value));
            lowered[argument.ParameterIndex] = Var(localName);
        }

        for (var i = 0; i < lowered.Length; i++)
        {
            if (lowered[i] is not null)
            {
                continue;
            }

            if (parameters[i].ExplicitDefaultValue is null &&
                parameters[i].Type.IsReferenceType)
            {
                throw new NotSupportedException(
                    $"{description} call cannot omit reference parameter '{parameters[i].Name}' because null cannot be represented in server extension payloads.");
            }

            lowered[i] = LiteralJson(parameters[i], parameters[i].ExplicitDefaultValue);
        }

        return lowered!;
    }

    private static bool CanBindArgumentsInParameterOrder(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters)
        => TryBindArgumentsInParameterOrder(arguments, parameters, description: null, out _);

    private static BoundRpcArguments BindArgumentsInParameterOrder(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string description)
    {
        if (TryBindArgumentsInParameterOrder(arguments, parameters, description, out var bound))
        {
            return bound;
        }

        throw new NotSupportedException($"{description} call has duplicate or misplaced arguments.");
    }

    private static bool TryBindArgumentsInParameterOrder(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string? description,
        out BoundRpcArguments bound)
    {
        bound = new BoundRpcArguments([], []);
        if (arguments.Count > parameters.Count)
        {
            return Fail(description, $" call must pass at most {parameters.Count} argument(s).");
        }

        var current = new (int ParameterIndex, ExpressionSyntax Expression)[arguments.Count];
        var assigned = new bool[parameters.Count];
        var nextPositional = 0;
        var hasOutOfPositionNamedArgument = false;
        for (var ordinal = 0; ordinal < arguments.Count; ordinal++)
        {
            if (!TryBindArgument(
                    arguments[ordinal],
                    ordinal,
                    nextPositional,
                    parameters,
                    description,
                    current,
                    assigned,
                    ref nextPositional,
                    ref hasOutOfPositionNamedArgument))
            {
                return false;
            }
        }

        if (!ValidateRequiredParameters(assigned, parameters, description))
        {
            return false;
        }

        bound = new BoundRpcArguments(assigned, current);
        return true;
    }

    private static bool TryBindArgument(
        ArgumentSyntax argument,
        int ordinal,
        int nextPositionalValue,
        IReadOnlyList<IParameterSymbol> parameters,
        string? description,
        (int ParameterIndex, ExpressionSyntax Expression)[] current,
        bool[] assigned,
        ref int nextPositional,
        ref bool hasOutOfPositionNamedArgument)
    {
        if (argument.RefKindKeyword.ValueText.Length != 0)
        {
            return Fail(description, " call cannot use ref, in, or out arguments.");
        }

        var index = BindArgumentIndex(
            argument,
            ordinal,
            nextPositionalValue,
            parameters,
            description,
            ref hasOutOfPositionNamedArgument);
        if (index < 0)
        {
            return false;
        }

        if (index >= parameters.Count || assigned[index])
        {
            return Fail(description, " call has duplicate or misplaced arguments.");
        }

        current[ordinal] = (index, argument.Expression);
        assigned[index] = true;
        nextPositional = NextUnassigned(assigned, nextPositional);
        return true;
    }

    private static int BindArgumentIndex(
        ArgumentSyntax argument,
        int ordinal,
        int nextPositional,
        IReadOnlyList<IParameterSymbol> parameters,
        string? description,
        ref bool hasOutOfPositionNamedArgument)
    {
        if (argument.NameColon is { } name)
        {
            var index = IndexOfParameter(parameters, name.Name.Identifier.ValueText, description);
            hasOutOfPositionNamedArgument |= index >= 0 && index != ordinal;
            return index;
        }

        if (hasOutOfPositionNamedArgument)
        {
            Fail(description, " call has duplicate or misplaced arguments.");
            return -1;
        }

        return nextPositional;
    }

    private static bool ValidateRequiredParameters(
        bool[] assigned,
        IReadOnlyList<IParameterSymbol> parameters,
        string? description)
    {
        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i] && !parameters[i].HasExplicitDefaultValue)
            {
                return Fail(description, $" call must pass parameter '{parameters[i].Name}'.");
            }
        }

        return true;
    }

    private static bool Fail(string? description, string message)
    {
        if (description is null)
        {
            return false;
        }

        throw new NotSupportedException(description + message);
    }

    private static int NextUnassigned(bool[] assigned, int start)
    {
        while (start < assigned.Length && assigned[start])
        {
            start++;
        }

        return start;
    }

    private static int IndexOfParameter(IReadOnlyList<IParameterSymbol> parameters, string name, string? description)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        if (description is null)
        {
            return -1;
        }

        throw new NotSupportedException($"{description} has no parameter '{name}'.");
    }

    private readonly record struct BoundRpcArguments(
        bool[] Assigned,
        IReadOnlyList<(int ParameterIndex, ExpressionSyntax Expression)> EvaluationOrder);
}
