using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string BitArrayTypeName = "System.Collections.BitArray";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IObjectCreationOperation creation, out string forbidden)
    {
        var method = creation.Constructor;
        if (method is not { MethodKind: MethodKind.Constructor } ||
            !HasCapacityParameter(method, creation))
        {
            forbidden = null!;
            return false;
        }

        var type = method.ContainingType;
        var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return TryGetForbiddenTypeDisplayName(type, typeName, out forbidden);
    }

    private static bool TryGetForbiddenTypeDisplayName(
        INamedTypeSymbol type,
        string typeName,
        out string forbidden)
    {
        forbidden = typeName switch
        {
            BitArrayTypeName => BitArrayTypeName,
            ListTypeName => "System.Collections.Generic.List",
            DictionaryTypeName => "System.Collections.Generic.Dictionary",
            QueueTypeName => "System.Collections.Generic.Queue",
            StackTypeName or HashSetTypeName or PriorityQueueTypeName =>
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            _ => null!
        };

        return forbidden is not null;
    }

    private static bool HasCapacityParameter(IMethodSymbol method, IObjectCreationOperation creation)
    {
        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        var capacityName = typeName switch
        {
            BitArrayTypeName => "length",
            PriorityQueueTypeName => "initialCapacity",
            _ => "capacity"
        };
        return method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal)) &&
            (typeName != BitArrayTypeName || HasNonZeroLengthArgument(creation));
    }

    private static bool HasNonZeroLengthArgument(IObjectCreationOperation creation)
        => creation.Arguments.Any(static argument =>
            argument.Parameter is { Type.SpecialType: SpecialType.System_Int32, Name: "length" } &&
            argument.Value.ConstantValue is not { HasValue: true, Value: 0 });
}
