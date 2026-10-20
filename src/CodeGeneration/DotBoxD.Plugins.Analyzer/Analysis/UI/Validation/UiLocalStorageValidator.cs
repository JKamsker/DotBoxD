using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

internal static class UiLocalStorageValidator
{
    public static void Validate(MethodDeclarationSyntax syntax, SemanticModel model, CancellationToken token)
    {
        var locals = new Dictionary<string, ILocalSymbol>(StringComparer.Ordinal);
        foreach (var node in syntax.DescendantNodes())
        {
            token.ThrowIfCancellationRequested();
            var symbol = node switch
            {
                VariableDeclaratorSyntax variable => model.GetDeclaredSymbol(variable, token),
                ForEachStatementSyntax loop => model.GetDeclaredSymbol(loop, token),
                _ => null
            };
            if (symbol is not ILocalSymbol local)
            { continue; }
            if (locals.TryGetValue(local.Name, out var previous) && !SymbolEqualityComparer.Default.Equals(previous, local))
            {
                throw new NotSupportedException($"local name '{local.Name}' is reused in distinct scopes; use unique local names for kernel function storage");
            }
            locals[local.Name] = local;
        }
    }
}
