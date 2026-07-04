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
