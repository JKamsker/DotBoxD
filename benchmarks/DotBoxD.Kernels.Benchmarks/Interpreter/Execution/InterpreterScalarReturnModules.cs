using System.Globalization;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarReturnModules
{
    public static string Create(
        ScalarReturnType type,
        ScalarReturnOperand operand,
        int operationCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operationCount);

        var typeName = type.ToString();
        var typeId = typeName.ToLowerInvariant();
        var operandId = operand == ScalarReturnOperand.Literal ? "literal" : "raw-variable";
        var parameters = operand == ScalarReturnOperand.Literal
            ? $$"""[{ "name": "value", "type": "{{typeName}}" }]"""
            : $$"""
              [
                { "name": "value", "type": "{{typeName}}" },
                { "name": "step", "type": "{{typeName}}" }
              ]
              """;
        var right = operand == ScalarReturnOperand.Literal
            ? Literal(type)
            : """{ "var": "step" }""";
        var expression = """{ "var": "value" }""";
        for (var i = 0; i < operationCount; i++)
        {
            expression = string.Create(
                CultureInfo.InvariantCulture,
                $"{{ \"op\": \"add\", \"left\": {expression}, \"right\": {right} }}");
        }

        return $$"""
        {
          "id": "interpreter-scalar-return-{{typeId}}-{{operandId}}-{{operationCount}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": {{parameters}},
            "returnType": "{{typeName}}",
            "body": [{ "op": "return", "value": {{expression}} }]
          }]
        }
        """;
    }

    private static string Literal(ScalarReturnType type)
        => type == ScalarReturnType.I64 ? """{ "i64": 3 }""" : """{ "f64": 0.25 }""";
}

internal enum ScalarReturnType
{
    I64,
    F64
}

internal enum ScalarReturnOperand
{
    Literal,
    RawVariable
}
