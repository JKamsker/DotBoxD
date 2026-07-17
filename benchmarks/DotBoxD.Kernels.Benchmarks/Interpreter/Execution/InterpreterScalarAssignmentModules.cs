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
            ? type == ScalarAssignmentType.I64 ? """{ "i64": 1 }""" : """{ "f64": 1.0 }"""
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
}

internal enum ScalarAssignmentType
{
    I64,
    F64
}

internal enum ScalarAssignmentRhs
{
    Literal,
    RawVariable
}
