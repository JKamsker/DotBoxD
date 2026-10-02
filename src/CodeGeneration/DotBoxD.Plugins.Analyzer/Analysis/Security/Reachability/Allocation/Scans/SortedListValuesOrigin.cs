using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class SortedListValuesOrigin
{
    public static bool IsMatch(IOperation? receiver, Compilation compilation)
        => IsMatch(receiver, compilation, new HashSet<ISymbol>(SymbolEqualityComparer.Default));

    private static bool IsMatch(IOperation? receiver, Compilation compilation, HashSet<ISymbol> visited)
    {
        while (receiver is IConversionOperation conversion)
        {
            receiver = conversion.Operand;
        }

        if (receiver is IPropertyReferenceOperation { Property.Name: "Values" } property)
        {
            var type = property.Property.ContainingType;
            return FrameworkCollectionIdentity.IsFrameworkType(type) &&
                   type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
                   "System.Collections.Generic.SortedList<TKey, TValue>";
        }

        if (receiver is not ILocalReferenceOperation local || !visited.Add(local.Local))
        {
            return false;
        }

        foreach (var reference in local.Local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } value })
            {
                var initializer = compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value);
                if (IsMatch(initializer, compilation, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
