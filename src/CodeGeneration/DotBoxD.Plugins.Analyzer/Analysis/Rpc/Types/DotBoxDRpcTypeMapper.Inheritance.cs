using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    /// <summary>
    /// Rejects DTOs that inherit public instance data members. <see cref="RecordFields"/> only sees declared
    /// members, so inherited members would otherwise be silently dropped from analyzer and runtime wire shapes.
    /// </summary>
    internal static void RejectInheritedDtoProperties(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
            {
                continue;
            }

            foreach (var member in baseType.GetMembers())
            {
                RejectInheritedDtoProperty(type, baseType, member);
                RejectInheritedDtoField(type, baseType, member);
            }
        }
    }

    private static void RejectInheritedDtoProperty(
        INamedTypeSymbol type,
        INamedTypeSymbol baseType,
        ISymbol member)
    {
        if (member is IPropertySymbol
            {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: false,
                GetMethod: not null,
                IsIndexer: false
            } property &&
            !property.IsImplicitlyDeclared &&
            !IsIgnoredDataMember(property))
        {
            throw new NotSupportedException(
                $"Server extension DTO '{type.ToDisplayString()}' must not inherit public properties from " +
                $"base type '{baseType.ToDisplayString()}'; flatten the DTO into a single type.");
        }
    }

    private static void RejectInheritedDtoField(
        INamedTypeSymbol type,
        INamedTypeSymbol baseType,
        ISymbol member)
    {
        if (member is IFieldSymbol
            {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: false,
                IsConst: false
            } field &&
            !field.IsImplicitlyDeclared &&
            !IsIgnoredDataMember(field))
        {
            throw new NotSupportedException(
                $"Server extension DTO '{type.ToDisplayString()}' must not inherit public fields from " +
                $"base type '{baseType.ToDisplayString()}'; flatten the DTO into a single type.");
        }
    }
}
