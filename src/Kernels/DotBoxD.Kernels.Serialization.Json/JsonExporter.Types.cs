using System.Text.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Serialization.Json;

internal static class JsonExporterTypeWriter
{
    public static void WriteType(Utf8JsonWriter writer, SandboxType type)
    {
        if (type.Arguments.Count == 0)
        {
            WriteStringValue(writer, type.Name, "type name");
            return;
        }

        writer.WriteStartObject();
        WriteString(writer, "name", type.Name, "type name");
        writer.WritePropertyName("arguments");
        writer.WriteStartArray();
        foreach (var argument in type.Arguments)
        {
            WriteType(writer, argument);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static void WriteString(Utf8JsonWriter writer, string propertyName, string value, string diagnosticName)
    {
        JsonStringSafety.RequireWellFormedUtf16(value, diagnosticName);
        writer.WriteString(propertyName, value);
    }

    public static void WriteStringValue(Utf8JsonWriter writer, string value, string diagnosticName)
    {
        JsonStringSafety.RequireWellFormedUtf16(value, diagnosticName);
        writer.WriteStringValue(value);
    }

    public static SandboxValidationException Error(string code, string message) => JsonExportNames.Error(code, message);
}

internal static class JsonExporterLiteralWriter
{
    public static void WriteOpaqueId(Utf8JsonWriter writer, OpaqueIdValue id)
    {
        if (!SandboxType.IsWellFormedOpaqueIdName(id.TypeName))
        {
            throw JsonExportNames.Error("E-JSON-ID", "'opaqueId.type' must be a well-formed opaque-id brand");
        }

        if (!SandboxLiteralConstraints.IsOpaqueId(id.Value))
        {
            throw JsonExportNames.Error("E-JSON-ID", "'opaqueId.value' must be a safe opaque-id value");
        }

        writer.WriteStartObject("opaqueId");
        JsonExporterTypeWriter.WriteString(writer, "type", id.TypeName, "opaque id type");
        JsonExporterTypeWriter.WriteString(writer, "value", id.Value, "opaque id value");
        writer.WriteEndObject();
    }

    public static void WritePath(Utf8JsonWriter writer, SandboxPathValue path)
    {
        if (path.Value?.RelativePath is not { } relativePath ||
            !SandboxLiteralConstraints.IsPortableRelativePath(relativePath))
        {
            throw JsonExportNames.Error("E-JSON-PATH", "'path' must be a portable relative path");
        }

        JsonExporterTypeWriter.WriteString(writer, "path", relativePath, "path");
    }

    public static void WriteUri(Utf8JsonWriter writer, SandboxUriValue uri)
    {
        if (uri.Value?.Value is not { } value ||
            !SandboxLiteralConstraints.IsSandboxUri(value))
        {
            throw JsonExportNames.Error("E-JSON-URI", "'uri' must be an absolute URI without user info");
        }

        JsonExporterTypeWriter.WriteString(writer, "uri", value, "uri");
    }
}
