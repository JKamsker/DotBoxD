using System.Text.Json;
using static SafeIR.JsonImport;

namespace SafeIR;

public static class SafeIrJsonImporter
{
    public static SandboxModule Import(string json)
    {
        try
        {
            JsonImportBudgetGuard.Validate(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });

            return ReadModule(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw Error("E-JSON-INVALID", ex.Message);
        }
        catch (FormatException ex)
        {
            throw Error("E-JSON-VERSION", ex.Message);
        }
    }

    private static SandboxModule ReadModule(JsonElement element)
    {
        RequireAllowedProperties(element, "module", ["id", "version", "targetSandboxVersion", "capabilityRequests", "functions", "metadata"]);
        RequireObject(element, "module root");
        var id = RequiredString(element, "id");
        var version = SemVersion.Parse(RequiredString(element, "version"));
        var target = OptionalString(element, "targetSandboxVersion") is { } targetText
            ? SemVersion.Parse(targetText)
            : SemVersion.One;

        return new SandboxModule(
            id,
            version,
            target,
            ReadCapabilityRequests(element),
            ReadFunctions(element),
            ReadMetadata(element));
    }

    private static IReadOnlyList<CapabilityRequest> ReadCapabilityRequests(JsonElement module)
    {
        if (!module.TryGetProperty("capabilityRequests", out var array))
        {
            return [];
        }

        RequireArray(array, "capabilityRequests");
        var requests = AllocateArray<CapabilityRequest>(array, out var count);
        if (count == 0) {
            return requests;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            RequireAllowedProperties(item, "capability request", ["id", "reason"]);
            requests[index++] = new CapabilityRequest(RequiredString(item, "id"), OptionalString(item, "reason"));
        }

        return requests;
    }

    private static IReadOnlyList<SandboxFunction> ReadFunctions(JsonElement module)
    {
        var array = RequiredArray(module, "functions");
        var functions = AllocateArray<SandboxFunction>(array, out var count);
        if (count == 0) {
            return functions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            functions[index++] = ReadFunction(item);
        }

        return functions;
    }

    private static SandboxFunction ReadFunction(JsonElement element)
    {
        RequireAllowedProperties(element, "function", ["id", "visibility", "parameters", "returnType", "body"]);
        RequireObject(element, "function");
        var visibility = OptionalString(element, "visibility") ?? "private";
        if (visibility is not "entrypoint" and not "private")
        {
            throw Error("E-JSON-VISIBILITY", $"unsupported function visibility '{visibility}'");
        }

        return new SandboxFunction(
            RequiredString(element, "id"),
            StringComparer.Ordinal.Equals(visibility, "entrypoint"),
            ReadParameters(element),
            ReadType(Required(element, "returnType")),
            ReadStatements(RequiredArray(element, "body")));
    }

    private static IReadOnlyList<Parameter> ReadParameters(JsonElement function)
    {
        if (!function.TryGetProperty("parameters", out var array))
        {
            return [];
        }

        RequireArray(array, "parameters");
        var parameters = AllocateArray<Parameter>(array, out var count);
        if (count == 0) {
            return parameters;
        }

        var index = 0;
        foreach (var parameter in array.EnumerateArray())
        {
            RequireAllowedProperties(parameter, "parameter", ["name", "type"]);
            parameters[index++] = new Parameter(
                RequiredString(parameter, "name"),
                ReadType(Required(parameter, "type")));
        }

        return parameters;
    }

    private static IReadOnlyList<Statement> ReadStatements(JsonElement array)
    {
        var statements = AllocateArray<Statement>(array, out var count);
        if (count == 0) {
            return statements;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            statements[index++] = ReadStatement(item);
        }

        return statements;
    }

    private static Statement ReadStatement(JsonElement element)
    {
        RequireObject(element, "statement");
        var op = RequiredString(element, "op");
        return op switch
        {
            "set" => ReadSetStatement(element),
            "return" => ReadReturnStatement(element),
            "expr" => ReadExpressionStatement(element),
            "if" => ReadIfStatement(element),
            "while" => ReadWhileStatement(element),
            "forRange" => ReadForRangeStatement(element),
            _ => throw Error("E-JSON-STATEMENT", $"unknown statement op '{op}'")
        };
    }

    private static AssignmentStatement ReadSetStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "set statement", ["op", "name", "value"]);
        return new AssignmentStatement(
                RequiredString(element, "name"),
                ReadExpression(Required(element, "value")),
                JsonSpan);
    }

    private static ReturnStatement ReadReturnStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "return statement", ["op", "value"]);
        return new ReturnStatement(ReadExpression(Required(element, "value")), JsonSpan);
    }

    private static ExpressionStatement ReadExpressionStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "expression statement", ["op", "value"]);
        return new ExpressionStatement(ReadExpression(Required(element, "value")), JsonSpan);
    }

    private static IfStatement ReadIfStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "if statement", ["op", "condition", "then", "else"]);
        return new IfStatement(
                ReadExpression(Required(element, "condition")),
                ReadStatements(RequiredArray(element, "then")),
                element.TryGetProperty("else", out var otherwise) ? ReadStatements(RequireArray(otherwise, "else")) : [],
                JsonSpan);
    }

    private static WhileStatement ReadWhileStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "while statement", ["op", "condition", "body"]);
        return new WhileStatement(
                ReadExpression(Required(element, "condition")),
                ReadStatements(RequiredArray(element, "body")),
                JsonSpan);
    }

    private static ForRangeStatement ReadForRangeStatement(JsonElement element)
    {
        RequireAllowedProperties(element, "forRange statement", ["op", "local", "start", "end", "body"]);
        return new ForRangeStatement(
                RequiredString(element, "local"),
                ReadExpression(Required(element, "start")),
                ReadExpression(Required(element, "end")),
                ReadStatements(RequiredArray(element, "body")),
                JsonSpan);
    }

    private static Expression ReadExpression(JsonElement element)
    {
        RequireObject(element, "expression");
        if (element.TryGetProperty("var", out var variable))
        {
            RequireAllowedProperties(element, "variable expression", ["var"]);
            return new VariableExpression(ReadStringValue(variable, "var"), JsonSpan);
        }

        if (JsonLiteralReader.TryRead(element, out var literal, out var literalName))
        {
            RequireAllowedProperties(element, "literal expression", [literalName]);
            return new LiteralExpression(literal, JsonSpan);
        }

        if (element.TryGetProperty("call", out var call))
        {
            RequireAllowedProperties(element, "call expression", ["call", "args", "genericType"]);
            return ReadCall(element, ReadStringValue(call, "call"));
        }

        if (element.TryGetProperty("unary", out var unary))
        {
            RequireAllowedProperties(element, "unary expression", ["unary", "operand"]);
            return new UnaryExpression(
                JsonOperatorReader.NormalizeUnary(ReadStringValue(unary, "unary")),
                ReadExpression(Required(element, "operand")),
                JsonSpan);
        }

        if (element.TryGetProperty("op", out var binary))
        {
            RequireAllowedProperties(element, "binary expression", ["op", "left", "right"]);
            return new BinaryExpression(
                ReadExpression(Required(element, "left")),
                JsonOperatorReader.NormalizeBinary(ReadStringValue(binary, "op")),
                ReadExpression(Required(element, "right")),
                JsonSpan);
        }

        throw Error("E-JSON-EXPR", "unknown expression shape");
    }

    private static CallExpression ReadCall(JsonElement element, string name)
    {
        var args = element.TryGetProperty("args", out var array)
            ? ReadExpressions(array)
            : [];
        var genericType = element.TryGetProperty("genericType", out var generic)
            ? ReadType(generic)
            : null;
        return new CallExpression(name, args, genericType, JsonSpan);
    }

    private static IReadOnlyList<Expression> ReadExpressions(JsonElement array)
    {
        RequireArray(array, "args");
        var expressions = AllocateArray<Expression>(array, out var count);
        if (count == 0) {
            return expressions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            expressions[index++] = ReadExpression(item);
        }

        return expressions;
    }

    private static SandboxType ReadType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var name = element.GetString() ?? "";
            if (name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal))
            {
                throw Error("E-JSON-TYPE", "generic types must be JSON objects, not strings");
            }

            return SandboxType.Scalar(name);
        }

        RequireObject(element, "type");
        RequireAllowedProperties(element, "type", ["name", "arguments"]);
        var arguments = element.TryGetProperty("arguments", out var args)
            ? ReadTypeArguments(args)
            : [];
        return new SandboxType(RequiredString(element, "name"), arguments);
    }

    private static IReadOnlyList<SandboxType> ReadTypeArguments(JsonElement array)
    {
        RequireArray(array, "type arguments");
        var arguments = AllocateArray<SandboxType>(array, out var count);
        if (count == 0) {
            return arguments;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            arguments[index++] = ReadType(item);
        }

        return arguments;
    }
    private static IReadOnlyDictionary<string, string> ReadMetadata(JsonElement module)
    {
        if (!module.TryGetProperty("metadata", out var metadata))
        {
            return new Dictionary<string, string>();
        }

        RequireObject(metadata, "metadata");
        RequireUniqueProperties(metadata, "metadata");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in metadata.EnumerateObject())
        {
            values.Add(property.Name, ReadStringValue(property.Value, $"metadata.{property.Name}"));
        }

        return values;
    }

}
