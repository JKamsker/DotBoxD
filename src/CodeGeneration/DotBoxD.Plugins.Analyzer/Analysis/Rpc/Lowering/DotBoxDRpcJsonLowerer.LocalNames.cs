using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    internal string LowerExpressionWithPrelude(ExpressionSyntax expression, List<string> output)
    {
        var previous = _expressionPrelude;
        var prelude = new List<string>();
        _expressionPrelude = prelude;
        try
        {
            var lowered = LowerExpression(expression);
            output.AddRange(prelude);
            return lowered;
        }
        finally
        {
            _expressionPrelude = previous;
        }
    }

    private void AddExpressionPrelude(string statement)
    {
        if (_expressionPrelude is null)
        {
            throw new NotSupportedException("Server extension expression prelude is not available.");
        }

        _expressionPrelude.Add(statement);
    }

    private string ReserveGeneratedLocal(string seed)
        => _reserveGeneratedName?.Invoke(seed) ?? ReserveGeneratedName(seed);

    private string ReserveGeneratedName(string seed)
    {
        var name = seed;
        for (var suffix = 0; _reservedNames.Contains(name); suffix++)
        {
            name = seed + "_" + suffix;
        }

        _reservedNames.Add(name);
        return name;
    }

    internal string NextDiscardLocal()
    {
        while (true)
        {
            var name = "__sir_discard" + _tempCounter++;
            if (_reservedNames.Add(name))
            {
                return name;
            }
        }
    }

    private static string StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal void ReserveUserNames(SyntaxNode node)
    {
        foreach (var token in node.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                _reservedNames.Add(token.ValueText);
            }
        }
    }

    internal int NextLoopTempSuffix()
    {
        while (true)
        {
            var suffix = _tempCounter++;
            if (_reservedNames.Contains("__sir_src" + suffix) ||
                _reservedNames.Contains("__sir_i" + suffix))
            {
                continue;
            }

            _reservedNames.Add("__sir_src" + suffix);
            _reservedNames.Add("__sir_i" + suffix);
            return suffix;
        }
    }
}
