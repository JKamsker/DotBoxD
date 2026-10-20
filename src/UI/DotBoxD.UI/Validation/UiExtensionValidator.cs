using System.Text;
using System.Text.Json;

namespace DotBoxD.UI;

internal static class UiExtensionValidator
{
    public static void Validate(UiPackage package, UiPolicy policy)
    {
        if (package.Extensions.IsDefault || package.Extensions.Length > policy.MaxNodes)
        { throw new UiValidationException("Missing or excessive UI extension declarations."); }
        var expected = package.Nodes.Where(n => n?.Primitive == UiPrimitive.Extension).Select(n => n.Id).ToHashSet();
        var found = new HashSet<int>();
        foreach (var extension in package.Extensions)
        {
            ValidateDeclaration(extension, expected, found, policy);
            ValidateJson(extension.PayloadJson);

        }
        if (!expected.SetEquals(found))
        { throw new UiValidationException("Every extension node requires exactly one declaration."); }
    }
    private static void ValidateDeclaration(UiExtension extension, HashSet<int> expected, HashSet<int> found, UiPolicy policy)
    {
        if (extension is null || !expected.Contains(extension.NodeId) || !found.Add(extension.NodeId) ||
            !policy.AllowedExtensionSchemas.Contains(extension.SchemaId, StringComparer.Ordinal) ||
            extension.PayloadJson is null || Encoding.UTF8.GetByteCount(extension.PayloadJson) > policy.MaxExtensionBytes)
        { throw new UiValidationException("Unknown, duplicate, disabled or excessive UI extension data."); }
    }

    private static void ValidateJson(string json)
    {
        try
        {
            using var payload = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
            { throw new UiValidationException("Extension payloads must be schema-validated JSON objects."); }
        }
        catch (JsonException)
        { throw new UiValidationException("Malformed UI extension JSON."); }
    }

}
