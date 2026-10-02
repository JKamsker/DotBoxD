using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class SortedListValuesOrigin
{
    public static bool IsMatch(IOperation? receiver, Compilation compilation)
        => IsMatch(receiver, compilation, new HashSet<SyntaxNode>());

    private static bool IsMatch(IOperation? receiver, Compilation compilation, HashSet<SyntaxNode> visited)
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

        if (receiver is not ILocalReferenceOperation local || !visited.Add(local.Syntax))
        {
            return false;
        }

        foreach (var value in LocalValueOrigins.GetValues(local, compilation))
        {
            var origin = compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value);
            if (IsMatch(origin, compilation, visited))
            {
                return true;
            }
        }

        return false;
    }
}
