using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private void ReserveUserNames(BlockSyntax block)
    {
        foreach (var token in block.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                _reservedNames.Add(token.ValueText);
            }
        }
    }

    private int NextLoopTempSuffix()
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
