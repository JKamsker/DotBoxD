using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcGeneratedClientAccessibility
{
    public static void EnsureAccessible(ITypeSymbol type, string description)
    {
        if (ContainsFileLocalType(type))
        {
            throw new NotSupportedException($"{description} is file-local and cannot be named from generated client code.");
        }

        if (!IsAccessible(type))
        {
            throw new NotSupportedException($"{description} must be accessible from generated client code.");
        }
    }

    private static bool IsAccessible(ITypeSymbol type)
        => type switch
        {
            INamedTypeSymbol named => IsNamedTypeAccessible(named),
            IArrayTypeSymbol array => IsAccessible(array.ElementType),
            IPointerTypeSymbol pointer => IsAccessible(pointer.PointedAtType),
            ITypeParameterSymbol or IDynamicTypeSymbol => true,
            _ => false
        };

    private static bool ContainsFileLocalType(ITypeSymbol type)
        => type switch
        {
            INamedTypeSymbol named => ContainsFileLocalNamedType(named),
            IArrayTypeSymbol array => ContainsFileLocalType(array.ElementType),
            IPointerTypeSymbol pointer => ContainsFileLocalType(pointer.PointedAtType),
            _ => false
        };

    private static bool ContainsFileLocalNamedType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return true;
            }

            foreach (var typeArgument in current.TypeArguments)
            {
                if (ContainsFileLocalType(typeArgument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNamedTypeAccessible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessibleFromGeneratedClient(current.DeclaredAccessibility))
            {
                return false;
            }

            foreach (var typeArgument in current.TypeArguments)
            {
                if (!IsAccessible(typeArgument))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAccessibleFromGeneratedClient(Accessibility accessibility)
        => accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
}
