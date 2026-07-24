using System.Globalization;
using System.Text;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarAssignmentModules
{
    public static string Create(ScalarAssignmentType type, ScalarAssignmentRhs rhs, int assignmentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentCount);

        var typeName = type.ToString();
        var typeId = typeName.ToLowerInvariant();
        var rhsId = rhs == ScalarAssignmentRhs.Literal ? "literal" : "raw-variable";
        var moduleId = string.Create(
            CultureInfo.InvariantCulture,
            $"interpreter-scalar-assignment-{typeId}-{rhsId}-{assignmentCount}");
        var parameters = rhs == ScalarAssignmentRhs.Literal
            ? $$"""[{ "name": "value", "type": "{{typeName}}" }]"""
            : $$"""
              [
                { "name": "value", "type": "{{typeName}}" },
                { "name": "step", "type": "{{typeName}}" }
              ]
              """;
        var right = rhs == ScalarAssignmentRhs.Literal
            ? Literal(type, "1")
            : """{ "var": "step" }""";
        var body = new StringBuilder();
        for (var i = 0; i < assignmentCount; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $$"""
                  {
                    "op": "set",
                    "name": "value",
                    "value": { "op": "add", "left": { "var": "value" }, "right": {{right}} }
                  },

                """);
        }

        body.Append("""      { "op": "return", "value": { "var": "value" } }""");
        return $$"""
            {
              "id": "{{moduleId}}",
              "version": "1.0.0",
              "functions": [{
                "id": "main",
                "visibility": "entrypoint",
                "parameters": {{parameters}},
                "returnType": "{{typeName}}",
                "body": [
            {{body}}
                ]
              }]
            }
            """;
    }

    public static string CreateBoxed(ScalarAssignmentType type, int assignmentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentCount);

        var typeName = type.ToString();
        var typeId = typeName.ToLowerInvariant();
        var body = new StringBuilder();
        for (var i = 0; i < assignmentCount; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $$"""
                  {
                    "op": "set",
                    "name": "result",
                    "value": {
                      "op": "add",
                      "left": {{Literal(type, "40")}},
                      "right": {{Literal(type, "2")}}
                    }
                  },

                """);
        }

        return $$"""
            {
              "id": "interpreter-scalar-assignment-{{typeId}}-boxed-{{assignmentCount}}",
              "version": "1.0.0",
              "functions": [{
                "id": "main",
                "visibility": "entrypoint",
                "parameters": [{
                  "name": "items",
                  "type": { "name": "List", "arguments": ["{{typeName}}"] }
                }],
                "returnType": "{{typeName}}",
                "body": [
                  { "op": "set", "name": "result", "value": {{Literal(type, "0")}} },
                  {
                    "op": "if",
                    "condition": { "bool": false },
                    "then": [{
                      "op": "set",
                      "name": "result",
                      "value": {
                        "call": "list.get",
                        "args": [{ "var": "items" }, { "i32": 0 }]
                      }
                    }],
                    "else": []
                  },
            {{body}}
                  { "op": "return", "value": { "var": "result" } }
                ]
              }]
            }
            """;
    }

    public static string CreateLocalHelper(ScalarAssignmentType type, int assignmentCount)
        => CreateLocalHelper(type, assignmentCount, repeatParameter: false);

    public static string CreateUnsupportedLocalHelper(ScalarAssignmentType type, int assignmentCount)
        => CreateLocalHelper(type, assignmentCount, repeatParameter: true);

    private static string CreateLocalHelper(
        ScalarAssignmentType type,
        int assignmentCount,
        bool repeatParameter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentCount);
        if (repeatParameter && type != ScalarAssignmentType.I32)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "unsupported helper control is I32-only");
        }

        var typeName = type.ToString();
        var typeId = typeName.ToLowerInvariant();
        var shapeId = repeatParameter ? "unsupported-local-helper" : "local-helper";
        var right = repeatParameter
            ? """{ "var": "value" }"""
            : Literal(type, "1");
        var body = new StringBuilder();
        for (var i = 0; i < assignmentCount; i++)
        {
            body.AppendLine("""
                  {
                    "op": "set",
                    "name": "value",
                    "value": { "call": "next", "args": [{ "var": "value" }] }
                  },
                """);
        }

        return $$"""
            {
              "id": "interpreter-scalar-assignment-{{typeId}}-{{shapeId}}-{{assignmentCount}}",
              "version": "1.0.0",
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [{ "name": "value", "type": "{{typeName}}" }],
                  "returnType": "{{typeName}}",
                  "body": [
            {{body}}
                    { "op": "return", "value": { "var": "value" } }
                  ]
                },
                {
                  "id": "next",
                  "visibility": "private",
                  "parameters": [{ "name": "value", "type": "{{typeName}}" }],
                  "returnType": "{{typeName}}",
                  "body": [{
                    "op": "return",
                    "value": {
                      "op": "add",
                      "left": { "var": "value" },
                      "right": {{right}}
                    }
                  }]
                }
              ]
            }
            """;
    }

    private static string Literal(ScalarAssignmentType type, string value)
        => type switch
        {
            ScalarAssignmentType.I32 => $$"""{ "i32": {{value}} }""",
            ScalarAssignmentType.I64 => $$"""{ "i64": {{value}} }""",
            ScalarAssignmentType.F64 => $$"""{ "f64": {{value}}.0 }""",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };
}

internal enum ScalarAssignmentType
{
    I32,
    I64,
    F64
}

internal enum ScalarAssignmentRhs
{
    Literal,
    RawVariable
}
