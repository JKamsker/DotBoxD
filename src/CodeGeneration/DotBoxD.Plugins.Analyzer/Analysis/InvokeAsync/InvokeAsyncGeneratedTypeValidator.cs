using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedTypeValidator
{
    private const int MaxTypeDepth = 8;

    public static void Validate(InvokeAsyncCallShape shape, Compilation compilation)
    {
        ValidateType(shape.ReturnType, compilation, "return type", 0, NewVisitingSet());
        foreach (var argumentType in shape.ArgumentTypes)
        {
            ValidateType(argumentType, compilation, "capture type", 0, NewVisitingSet());
        }

        if (shape.CaptureType is { } captureType)
        {
            ValidateType(captureType, compilation, "capture bag type", 0, NewVisitingSet());
        }

        foreach (var syncOut in shape.SyncOuts)
        {
            ValidateType(syncOut.Type, compilation, "capture member '" + syncOut.TargetName + "'", 0, NewVisitingSet());
        }
    }

    private static void ValidateType(
        ITypeSymbol type,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        RejectNullableType(type, role);

        if (TryValidateArrayType(type, compilation, role, depth, visiting))
        {
            return;
        }

        RejectTypeParameter(type, role);

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        ValidateNamedTypeHeader(named, compilation, role, depth, visiting);

        if (IsTerminalWireType(type))
        {
            return;
        }

        if (TryValidateCollectionType(type, compilation, role, depth, visiting))
        {
            return;
        }

        ValidateRecordDtoType(type, named, compilation, role, depth, visiting);
    }

    private static void RejectNullableType(ITypeSymbol type, string role)
    {
        if (DotBoxDNullableScalarType.IsNullableValueType(type) ||
            type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' cannot be nullable because kernel RPC does not encode null values.");
        }
    }

    private static bool TryValidateArrayType(
        ITypeSymbol type,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        if (type is not IArrayTypeSymbol array)
        {
            return false;
        }

        RejectTooDeep(type, role, depth);
        ValidateType(array.ElementType, compilation, role + " element", depth + 1, visiting);
        return true;
    }

    private static void RejectTypeParameter(ITypeSymbol type, string role)
    {
        if (type.TypeKind == TypeKind.TypeParameter)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' must be a concrete generated-code-accessible type.");
        }
    }

    private static void ValidateNamedTypeHeader(
        INamedTypeSymbol named,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        RejectAnonymousType(named, role);
        RejectFileLocalType(named, role);
        RejectInaccessibleType(named, compilation, role);
        foreach (var typeArgument in named.TypeArguments)
        {
            ValidateType(typeArgument, compilation, role + " type argument", depth + 1, visiting);
        }
    }

    private static void RejectAnonymousType(INamedTypeSymbol named, string role)
    {
        if (named.IsAnonymousType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{named.ToDisplayString()}' cannot be anonymous because generated interceptors must name the type.");
        }
    }

    private static void RejectFileLocalType(INamedTypeSymbol named, string role)
    {
        if (FileLocalType(named) is { } fileLocalType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{fileLocalType.ToDisplayString()}' is file-local; generated interceptors and readers cannot name file-local types.");
        }
    }

    private static void RejectInaccessibleType(INamedTypeSymbol named, Compilation compilation, string role)
    {
        if (!compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, compilation.Assembly))
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{named.ToDisplayString()}' must be accessible from generated code.");
        }
    }

    private static bool IsTerminalWireType(ITypeSymbol type)
        => DotBoxDRpcTypeMapper.IsScalar(type) ||
           DotBoxDRpcTypeMapper.IsGuid(type) ||
           DotBoxDRpcTypeMapper.IsDateTimeWireType(type) ||
           DotBoxDRpcTypeMapper.IsTimeSpanWireType(type) ||
           type.TypeKind == TypeKind.Enum;

    private static bool TryValidateCollectionType(
        ITypeSymbol type,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            RejectTooDeep(type, role, depth);
            ValidateType(elementType, compilation, role + " element", depth + 1, visiting);
            return true;
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            RejectTooDeep(type, role, depth);
            ValidateType(map.Key, compilation, role + " key", depth + 1, visiting);
            ValidateType(map.Value, compilation, role + " value", depth + 1, visiting);
            return true;
        }

        return false;
    }

    private static void ValidateRecordDtoType(
        ITypeSymbol type,
        INamedTypeSymbol named,
        Compilation compilation,
        string role,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        if (!DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return;
        }

        RejectTooDeep(type, role, depth);
        RejectRecursiveDto(type, named, role, visiting);
        try
        {
            foreach (var field in DotBoxDRpcTypeMapper.RecordFields(named))
            {
                ValidateType(field.Type, compilation, role + " member '" + field.Name + "'", depth + 1, visiting);
            }
        }
        finally
        {
            visiting.Remove(named);
        }
    }

    private static void RejectRecursiveDto(
        ITypeSymbol type,
        INamedTypeSymbol named,
        string role,
        HashSet<ITypeSymbol> visiting)
    {
        if (!visiting.Add(named))
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' is cyclic; recursive DTO shapes are not supported.");
        }
    }

    private static void RejectTooDeep(ITypeSymbol type, string role, int depth)
    {
        if (depth >= MaxTypeDepth)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' exceeds the supported RPC shape depth.");
        }
    }

    private static HashSet<ITypeSymbol> NewVisitingSet()
        => new(SymbolEqualityComparer.Default);

    private static INamedTypeSymbol? FileLocalType(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            // Nested types inherit the file-scoped visibility of an enclosing file-local type.
            if (current.IsFileLocal)
            {
                return current;
            }
        }

        return null;
    }
}
