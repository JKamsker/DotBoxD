using System.Buffers;
using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Serialization.Json;

public static class JsonExporter
{
    public static string Export(SandboxModule module, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented });
        Write(writer, module);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static void Write(Utf8JsonWriter writer, SandboxModule module)
    {
        writer.WriteStartObject();
        WriteString(writer, "id", module.Id, "module id");
        writer.WriteString("version", module.Version.ToString());
        writer.WriteString("targetSandboxVersion", module.TargetSandboxVersion.ToString());
        WriteCapabilityRequests(writer, module.CapabilityRequests);
        WriteMetadata(writer, module.Metadata);
        WriteFunctions(writer, module.Functions);
        writer.WriteEndObject();
    }

    private static void WriteCapabilityRequests(Utf8JsonWriter writer, IReadOnlyList<CapabilityRequest> requests)
    {
        writer.WritePropertyName("capabilityRequests");
        writer.WriteStartArray();
        foreach (var request in requests)
        {
            writer.WriteStartObject();
            WriteString(writer, "id", request.Id, "capability request id");
            if (request.Reason is not null)
            {
                WriteString(writer, "reason", request.Reason, "capability request reason");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> metadata)
    {
        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        foreach (var item in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            JsonStringSafety.RequireWellFormedUtf16(item.Key, "metadata key");
            JsonStringSafety.RequireWellFormedUtf16(item.Value, "metadata value");
            writer.WriteString(item.Key, item.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteFunctions(Utf8JsonWriter writer, IReadOnlyList<SandboxFunction> functions)
    {
        writer.WritePropertyName("functions");
        writer.WriteStartArray();
        foreach (var function in functions)
        {
            WriteFunction(writer, function);
        }

        writer.WriteEndArray();
    }

    private static void WriteFunction(Utf8JsonWriter writer, SandboxFunction function)
    {
        writer.WriteStartObject();
        WriteString(writer, "id", function.Id, "function id");
        writer.WriteString("visibility", function.IsEntrypoint ? "entrypoint" : "private");
        WriteParameters(writer, function.Parameters);
        writer.WritePropertyName("returnType");
        WriteType(writer, function.ReturnType);
        writer.WritePropertyName("body");
        WriteStatements(writer, function.Body);
        writer.WriteEndObject();
    }

    private static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<Parameter> parameters)
    {
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        foreach (var parameter in parameters)
        {
            writer.WriteStartObject();
            WriteString(writer, "name", parameter.Name, "parameter name");
            writer.WritePropertyName("type");
            WriteType(writer, parameter.Type);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStatements(Utf8JsonWriter writer, IReadOnlyList<Statement> statements)
    {
        writer.WriteStartArray();
        foreach (var statement in statements)
        {
            WriteStatement(writer, statement);
        }

        writer.WriteEndArray();
    }

    private static void WriteStatement(Utf8JsonWriter writer, Statement statement)
    {
        writer.WriteStartObject();
        switch (statement)
        {
            case AssignmentStatement assignment:
                writer.WriteString("op", "set");
                WriteString(writer, "name", assignment.Name, "assignment local name");
                writer.WritePropertyName("value");
                WriteExpression(writer, assignment.Value);
                break;
            case ReturnStatement ret:
                writer.WriteString("op", "return");
                writer.WritePropertyName("value");
                WriteExpression(writer, ret.Value);
                break;
            case ExpressionStatement expression:
                writer.WriteString("op", "expr");
                writer.WritePropertyName("value");
                WriteExpression(writer, expression.Value);
                break;
            case IfStatement branch:
                writer.WriteString("op", "if");
                writer.WritePropertyName("condition");
                WriteExpression(writer, branch.Condition);
                writer.WritePropertyName("then");
                WriteStatements(writer, branch.Then);
                writer.WritePropertyName("else");
                WriteStatements(writer, branch.Else);
                break;
            case WhileStatement loop:
                writer.WriteString("op", "while");
                writer.WritePropertyName("condition");
                WriteExpression(writer, loop.Condition);
                writer.WritePropertyName("body");
                WriteStatements(writer, loop.Body);
                break;
            case ForRangeStatement range:
                writer.WriteString("op", "forRange");
                WriteString(writer, "local", range.LocalName, "for-range local name");
                writer.WritePropertyName("start");
                WriteExpression(writer, range.Start);
                writer.WritePropertyName("end");
                WriteExpression(writer, range.End);
                writer.WritePropertyName("body");
                WriteStatements(writer, range.Body);
                break;
            case ContinueStatement:
                writer.WriteString("op", "continue");
                break;
            case BreakStatement:
                writer.WriteString("op", "break");
                break;
            default:
                throw Error("E-JSON-EXPORT", $"statement type '{statement.GetType().Name}' cannot be exported");
        }

        writer.WriteEndObject();
    }

    private static void WriteExpression(Utf8JsonWriter writer, Expression expression)
    {
        writer.WriteStartObject();
        switch (expression)
        {
            case VariableExpression variable:
                WriteString(writer, "var", variable.Name, "variable name");
                break;
            case LiteralExpression literal:
                WriteLiteral(writer, literal.Value);
                break;
            case CallExpression call:
                WriteCall(writer, call);
                break;
            case UnaryExpression unary:
                writer.WriteString("unary", JsonExportNames.UnaryOperator(unary.Operator));
                writer.WritePropertyName("operand");
                WriteExpression(writer, unary.Operand);
                break;
            case BinaryExpression binary:
                writer.WriteString("op", JsonExportNames.BinaryOperator(binary.Operator));
                writer.WritePropertyName("left");
                WriteExpression(writer, binary.Left);
                writer.WritePropertyName("right");
                WriteExpression(writer, binary.Right);
                break;
            default:
                throw Error("E-JSON-EXPORT", $"expression type '{expression.GetType().Name}' cannot be exported");
        }

        writer.WriteEndObject();
    }

    private static void WriteCall(Utf8JsonWriter writer, CallExpression call)
    {
        WriteString(writer, "call", call.Name, "call name");
        if (call.GenericType is not null)
        {
            writer.WritePropertyName("genericType");
            WriteType(writer, call.GenericType);
        }

        writer.WritePropertyName("args");
        writer.WriteStartArray();
        foreach (var argument in call.Arguments)
        {
            WriteExpression(writer, argument);
        }

        writer.WriteEndArray();
    }

    private static void WriteLiteral(Utf8JsonWriter writer, SandboxValue value)
    {
        switch (value)
        {
            case UnitValue:
                writer.WriteBoolean("unit", true);
                break;
            case BoolValue boolean:
                writer.WriteBoolean("bool", boolean.Value);
                break;
            case I32Value integer:
                writer.WriteNumber("i32", integer.Value);
                break;
            case I64Value integer:
                writer.WriteNumber("i64", integer.Value);
                break;
            case F64Value number:
                writer.WriteNumber("f64", number.Value);
                break;
            case StringValue text:
                JsonStringSafety.RequireWellFormedUtf16(text.Value, "string");
                writer.WriteString("string", text.Value);
                break;
            case GuidValue guid:
                // Canonical hyphenated form (Guid.ToString() == "D"); JsonLiteralReader parses it back exactly.
                writer.WriteString("guid", guid.Value.ToString());
                break;
            case OpaqueIdValue id:
                writer.WriteStartObject("opaqueId");
                WriteString(writer, "type", id.TypeName, "opaque id type");
                WriteString(writer, "value", id.Value, "opaque id value");
                writer.WriteEndObject();
                break;
            case SandboxPathValue path:
                WriteString(writer, "path", path.Value.RelativePath, "path");
                break;
            case SandboxUriValue uri:
                WriteString(writer, "uri", uri.Value.Value, "uri");
                break;
            default:
                throw JsonExportNames.Error("E-JSON-EXPORT", $"literal type '{value.GetType().Name}' cannot be exported");
        }
    }

    private static void WriteType(Utf8JsonWriter writer, SandboxType type)
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

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string value, string diagnosticName)
    {
        JsonStringSafety.RequireWellFormedUtf16(value, diagnosticName);
        writer.WriteString(propertyName, value);
    }

    private static void WriteStringValue(Utf8JsonWriter writer, string value, string diagnosticName)
    {
        JsonStringSafety.RequireWellFormedUtf16(value, diagnosticName);
        writer.WriteStringValue(value);
    }

    private static SandboxValidationException Error(string code, string message) => JsonExportNames.Error(code, message);
}
