namespace SafeIR.Verifier;

using System.Collections.Immutable;
using System.Reflection.Metadata;

internal sealed class MethodSignatureNameProvider : ISignatureTypeProvider<string, object?>
{
    public static MethodSignatureNameProvider Instance { get; } = new();

    public string GetArrayType(string elementType, ArrayShape shape)
        => elementType + "[]";

    public string GetByReferenceType(string elementType)
        => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "fnptr";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index)
        => "!!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetGenericTypeParameter(object? genericContext, int index)
        => "!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired)
        => unmodifiedType;

    public string GetPinnedType(string elementType)
        => elementType;

    public string GetPointerType(string elementType)
        => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        => typeCode switch {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Object => "System.Object",
            _ => typeCode.ToString()
        };

    public string GetSZArrayType(string elementType)
        => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => MetadataName.TypeDefinition(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => MetadataName.TypeReference(reader, handle);

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
