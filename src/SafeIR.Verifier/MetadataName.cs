namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class MetadataName
{
    public static string TypeReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string TypeDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static (string TypeName, string MemberName) Member(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MemberReference) {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return (ParentName(reader, member.Parent), reader.GetString(member.Name));
        }

        if (handle.Kind == HandleKind.MethodDefinition) {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return ("", reader.GetString(method.Name));
        }

        if (handle.Kind == HandleKind.MethodSpecification) {
            var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            return Member(reader, spec.Method);
        }

        return ("", handle.Kind.ToString());
    }

    private static string ParentName(MetadataReader reader, EntityHandle parent)
        => parent.Kind switch {
            HandleKind.TypeReference => TypeReference(reader, (TypeReferenceHandle)parent),
            HandleKind.TypeDefinition => TypeDefinition(reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeSpecification => "TypeSpecification",
            _ => parent.Kind.ToString()
        };
}
