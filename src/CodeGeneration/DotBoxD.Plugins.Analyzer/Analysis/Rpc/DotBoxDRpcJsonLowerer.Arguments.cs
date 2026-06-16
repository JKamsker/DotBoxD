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
        if (arguments.Count != parameters.Count)
        {
            throw new NotSupportedException($"{description} call must pass {parameters.Count} argument(s).");
        }

        var lowered = new string[parameters.Count];
        var assigned = new bool[parameters.Count];
        var nextPositional = 0;
        foreach (var argument in arguments)
        {
            var index = argument.NameColon is { } name
                ? IndexOfParameter(parameters, name.Name.Identifier.ValueText, description)
                : nextPositional;
            if (index >= parameters.Count || assigned[index])
            {
                throw new NotSupportedException($"{description} call has duplicate or misplaced arguments.");
            }

            lowered[index] = LowerExpression(argument.Expression);
            assigned[index] = true;
            if (argument.NameColon is null)
            {
                nextPositional = NextUnassigned(assigned, index + 1);
            }
        }

        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i])
            {
                throw new NotSupportedException($"{description} call must pass parameter '{parameters[i].Name}'.");
            }
        }

        return lowered;
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
