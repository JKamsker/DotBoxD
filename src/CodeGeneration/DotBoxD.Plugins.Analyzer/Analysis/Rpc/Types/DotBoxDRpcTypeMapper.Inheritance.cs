using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    /// <summary>
    /// Returns the DTO inheritance hierarchy in deterministic base-to-derived order. Framework roots do not
    /// contribute DTO fields.
    /// </summary>
    private static IReadOnlyList<INamedTypeSymbol> DtoHierarchy(INamedTypeSymbol type)
    {
        var hierarchy = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType))
            {
                hierarchy.Add(current);
            }
        }

        hierarchy.Reverse();
        return hierarchy;
    }

    private static int OverriddenPropertyIndex(
        IReadOnlyList<RecordMember> members,
        IPropertySymbol property)
    {
        for (var overridden = property.OverriddenProperty;
             overridden is not null;
             overridden = overridden.OverriddenProperty)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(members[i].Symbol, overridden))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static void RejectDuplicateRecordMember(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> members,
        string name)
    {
        foreach (var member in members)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' has multiple public data members named " +
                    $"'{name}' across its inheritance hierarchy; hide or ignore one member explicitly.");
            }
        }
    }
}
