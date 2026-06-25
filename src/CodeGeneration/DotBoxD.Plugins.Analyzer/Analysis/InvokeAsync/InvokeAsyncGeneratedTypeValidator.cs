using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedTypeValidator
{
    public static void Validate(InvokeAsyncCallShape shape, Compilation compilation)
    {
        ValidateType(shape.ReturnType, compilation, "return type");
        foreach (var argumentType in shape.ArgumentTypes)
        {
            ValidateType(argumentType, compilation, "capture type");
        }

        if (shape.CaptureType is { } captureType)
        {
            ValidateType(captureType, compilation, "capture bag type");
        }

        foreach (var syncOut in shape.SyncOuts)
        {
            ValidateType(syncOut.Type, compilation, "capture member '" + syncOut.TargetName + "'");
        }
    }

    private static void ValidateType(ITypeSymbol type, Compilation compilation, string role)
    {
        if (type is IArrayTypeSymbol array)
        {
            ValidateType(array.ElementType, compilation, role + " element");
            return;
        }

        if (type.TypeKind == TypeKind.TypeParameter)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' must be a concrete generated-code-accessible type.");
        }

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        if (named.IsAnonymousType)
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' cannot be anonymous because generated interceptors must name the type.");
        }

        if (!compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, compilation.Assembly))
        {
            throw new NotSupportedException(
                $"InvokeAsync {role} '{type.ToDisplayString()}' must be accessible from generated code.");
        }

        foreach (var typeArgument in named.TypeArguments)
        {
            ValidateType(typeArgument, compilation, role + " type argument");
        }

        if (DotBoxDRpcTypeMapper.IsScalar(type) ||
            DotBoxDRpcTypeMapper.IsGuid(type) ||
            type.TypeKind == TypeKind.Enum)
        {
            return;
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            ValidateType(elementType, compilation, role + " element");
            return;
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            ValidateType(map.Key, compilation, role + " key");
            ValidateType(map.Value, compilation, role + " value");
            return;
        }

        if (DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            foreach (var field in DotBoxDRpcTypeMapper.RecordFields(named))
            {
                ValidateType(field.Type, compilation, role + " member '" + field.Name + "'");
            }
        }
    }
}
