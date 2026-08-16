using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ImmutableArrayCapacityPolicy
{
    private const string ImmutableArrayTypeName = "System.Collections.Immutable.ImmutableArray";
    private const string ImmutableArrayBuilderTypeName =
        "System.Collections.Immutable.ImmutableArray<T>.Builder";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (IsCreateBuilder(method, typeName))
        {
            forbidden = ImmutableArrayTypeName;
            return true;
        }

        if (IsBuilderAddRange(method, typeName))
        {
            forbidden =
                $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.{method.Name}";
            return true;
        }

        forbidden = null!;
        return false;
    }

    public static bool TryGetPropertyDisplayName(string typeName, string propertyName, out string forbidden)
    {
        if (string.Equals(typeName, ImmutableArrayBuilderTypeName, StringComparison.Ordinal) &&
            string.Equals(propertyName, "Capacity", StringComparison.Ordinal))
        {
            forbidden = ImmutableArrayTypeName;
            return true;
        }

        forbidden = null!;
        return false;
    }

    public static bool IsCapacityAllocationMethod(IMethodSymbol method, string typeName)
        => IsCreateBuilder(method, typeName) && HasInitialCapacityParameter(method) ||
           IsBuilderAddRange(method, typeName);

    private static bool IsCreateBuilder(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, Name: "CreateBuilder" } &&
           string.Equals(typeName, ImmutableArrayTypeName, StringComparison.Ordinal);

    private static bool IsBuilderAddRange(IMethodSymbol method, string typeName)
        => method is { IsStatic: false, Name: "AddRange" } &&
           string.Equals(typeName, ImmutableArrayBuilderTypeName, StringComparison.Ordinal);

    private static bool HasInitialCapacityParameter(IMethodSymbol method)
        => method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, "initialCapacity", StringComparison.Ordinal));
}
