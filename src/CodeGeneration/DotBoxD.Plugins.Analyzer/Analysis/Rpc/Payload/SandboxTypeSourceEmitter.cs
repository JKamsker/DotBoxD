using Microsoft.CodeAnalysis;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Emits the C# expression that constructs the <c>SandboxType</c> for a marshaller-eligible CLR type, mirroring
/// the runtime <c>KernelRpcMarshaller.SandboxTypeOf</c> so a lowered remote chain's kernel parameter and
/// projection return types line up exactly with the values the runtime convention adapter produces:
/// scalars and <see cref="System.Guid"/> to their sandbox scalar, enums to <c>I32</c>/<c>I64</c> by underlying
/// width, <c>List&lt;T&gt;</c>/<c>T[]</c> to <c>List</c>, <c>Dictionary&lt;K,V&gt;</c> to <c>Map</c>, and a DTO
/// record to a positional <c>Record</c>. Anything outside that set is not wire-eligible.
/// </summary>
internal static class SandboxTypeSourceEmitter
{
    private const string SandboxType = TypeNames.GlobalSandboxType;
    private static readonly ManifestTagResolver[] ManifestTagResolvers =
    [
        TrySpecialManifestTag,
        SandboxFrameworkTypeSource.ManifestTag,
        TryEnumManifestTag,
        TryCollectionManifestTag,
    ];

    private static readonly SandboxSourceResolver[] SandboxSourceResolvers =
    [
        TrySpecialSandboxSource,
        TryFrameworkSandboxSource,
        TryNullableSandboxSource,
        TryEnumSandboxSource,
        TryListSandboxSource,
        TryMapSandboxSource,
        TryRecordSandboxSource,
    ];

    private delegate string? ManifestTagResolver(ITypeSymbol type);

    private delegate string? SandboxSourceResolver(SandboxSourceContext context);

    // A record field whose type leads back to an enclosing record (directly or through a list/map/record) would
    // recurse forever, so the depth of a record/list/map nesting chain is bounded. The bound is kept at or below
    // the kernel verifier's structural depth limit (SandboxType.IsKnown defaults to maxDepth 8) so a type the
    // analyzer emits is never rejected at install as "unknown"; anything deeper is treated as not
    // marshaller-eligible and the chain fails safe at generation.
    private const int MaxDepth = 8;

    /// <summary>The <c>SandboxType</c> construction source for <paramref name="type"/>, or <c>null</c> when it
    /// is not marshaller-eligible (so the caller fails the chain safe rather than emitting an invalid kernel).</summary>
    public static string? TryEmit(ITypeSymbol type)
    {
        try
        {
            return Emit(type, 0);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The coarse manifest tag the expression lowerer carries for an event property of
    /// <paramref name="type"/>: a scalar token for scalars (enums reuse their underlying integer token), a
    /// non-scalar shape tag for Guid/list/map/record, or <see cref="ManifestTypes.Unsupported"/> when the type
    /// cannot be marshalled. Tag eligibility is kept in lockstep with <see cref="TryEmit"/>.</summary>
    public static string ManifestTag(ITypeSymbol type)
    {
        if (TryEmit(type) is null)
        {
            return ManifestTypes.Unsupported;
        }

        foreach (var resolver in ManifestTagResolvers)
        {
            if (resolver(type) is { } tag)
            {
                return tag;
            }
        }

        return ManifestTypes.Record;
    }

    private static string Emit(ITypeSymbol type, int depth)
    {
        var context = new SandboxSourceContext(type, depth);
        foreach (var resolver in SandboxSourceResolvers)
        {
            if (resolver(context) is { } source)
            {
                return source;
            }
        }

        throw new NotSupportedException();
    }

    private static string? TrySpecialManifestTag(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => ManifestTypes.Bool,
            SpecialType.System_Int32 => ManifestTypes.Int,
            SpecialType.System_Int64 => ManifestTypes.Long,
            SpecialType.System_Double => ManifestTypes.Double,
            SpecialType.System_Single => ManifestTypes.Double,
            SpecialType.System_String => ManifestTypes.String,
            _ => null,
        };

    private static string? TryEnumManifestTag(ITypeSymbol type)
        => type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType
            ? DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? ManifestTypes.Long : ManifestTypes.Int
            : null;

    private static string? TryCollectionManifestTag(ITypeSymbol type)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return ManifestTypes.List;
        }

        return DotBoxDRpcTypeMapper.MapTypes(type) is not null ? ManifestTypes.Map : null;
    }

    private static string? TrySpecialSandboxSource(SandboxSourceContext context)
        => context.Type.SpecialType switch
        {
            SpecialType.System_Boolean => SandboxType + ".Bool",
            SpecialType.System_Int32 => SandboxType + ".I32",
            SpecialType.System_Int64 => SandboxType + ".I64",
            SpecialType.System_Double => SandboxType + ".F64",
            SpecialType.System_Single => SandboxType + ".F64",
            SpecialType.System_String => SandboxType + ".String",
            _ => null,
        };

    private static string? TryFrameworkSandboxSource(SandboxSourceContext context)
        => SandboxFrameworkTypeSource.SandboxSource(context.Type, context.Depth);

    private static string? TryNullableSandboxSource(SandboxSourceContext context)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(context.Type, out var nullableUnderlying))
        {
            return null;
        }

        RejectNestedShapeAtDepth(context.Depth);
        return $"{SandboxType}.Record(new {SandboxType}[] {{ {SandboxType}.Bool, {Emit(nullableUnderlying, context.Depth + 1)} }})";
    }

    private static string? TryEnumSandboxSource(SandboxSourceContext context)
        => context.Type.TypeKind == TypeKind.Enum && context.Type is INamedTypeSymbol enumType
            ? DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? SandboxType + ".I64" : SandboxType + ".I32"
            : null;

    private static string? TryListSandboxSource(SandboxSourceContext context)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(context.Type) is not { } elementType)
        {
            return null;
        }

        RejectNestedShapeAtDepth(context.Depth);
        return $"{SandboxType}.List({Emit(elementType, context.Depth + 1)})";
    }

    private static string? TryMapSandboxSource(SandboxSourceContext context)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(context.Type) is not { } map)
        {
            return null;
        }

        RejectNestedShapeAtDepth(context.Depth);
        RejectUnsupportedMapKey(map.Key);
        return $"{SandboxType}.Map({Emit(map.Key, context.Depth + 1)}, {Emit(map.Value, context.Depth + 1)})";
    }

    private static string? TryRecordSandboxSource(SandboxSourceContext context)
    {
        if (context.Type is not INamedTypeSymbol named || !DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return null;
        }

        RejectNestedShapeAtDepth(context.Depth);
        return RecordSandboxSource(named, context.Depth);
    }

    private static string RecordSandboxSource(INamedTypeSymbol named, int depth)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var fieldTypes = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            fieldTypes[i] = Emit(fields[i].Type, depth + 1);
        }

        return $"{SandboxType}.Record(new {SandboxType}[] {{ {string.Join(", ", fieldTypes)} }})";
    }

    private static void RejectUnsupportedMapKey(ITypeSymbol key)
    {
        if (!DotBoxDRpcTypeMapper.IsSupportedMapKey(key))
        {
            throw new NotSupportedException();
        }
    }

    private static void RejectNestedShapeAtDepth(int depth)
    {
        if (depth >= MaxDepth)
        {
            throw new NotSupportedException();
        }
    }

    private readonly record struct SandboxSourceContext(ITypeSymbol Type, int Depth);
}
