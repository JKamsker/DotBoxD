using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    internal void ValidateUninitializedLocalDeclaration(VariableDeclaratorSyntax declarator)
    {
        var local = _model.GetDeclaredSymbol(declarator, _cancellationToken) as ILocalSymbol
            ?? throw new NotSupportedException(
                $"Server extension local '{declarator.Identifier.ValueText}' could not be resolved.");

        if (HasRpcServiceAttribute(local.Type))
        {
            throw new NotSupportedException(
                $"Scoped service handle local '{declarator.Identifier.ValueText}' must be initialized at declaration.");
        }
    }
}
