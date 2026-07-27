using System.Globalization;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.ScalarReturns;

internal static class ScalarReturnTestModules
{
    public static string Recurrences(
        string id,
        string type,
        string literalName,
        string increment,
        int operationCount,
        bool useRawStep)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operationCount);

        var parameters = useRawStep
            ? $$"""
              [
                { "name": "value", "type": "{{type}}" },
                { "name": "step", "type": "{{type}}" }
              ]
              """
            : $$"""[{ "name": "value", "type": "{{type}}" }]""";
        var right = useRawStep
            ? """{ "var": "step" }"""
            : $$"""{ "{{literalName}}": {{increment}} }""";
        var expression = """{ "var": "value" }""";
        for (var i = 0; i < operationCount; i++)
        {
            expression = string.Create(
                CultureInfo.InvariantCulture,
                $"{{ \"op\": \"add\", \"left\": {expression}, \"right\": {right} }}");
        }

        return Module(id, parameters, type, expression);
    }

    public static string Expression(string id, string type, string expression)
        => Module(
            id,
            $$"""[{ "name": "value", "type": "{{type}}" }]""",
            type,
            expression);

    public static string Literal(string id, string type, string literalName, string value)
        => Module(
            id,
            $$"""[{ "name": "value", "type": "{{type}}" }]""",
            type,
            $$"""{ "{{literalName}}": {{value}} }""");

    public static string NoArgumentExpression(string id, string returnType, string expression)
        => Module(id, "[]", returnType, expression);

    public static string Custom(
        string id,
        string parameters,
        string returnType,
        string expression)
        => Module(id, parameters, returnType, expression);

    private static string Module(
        string id,
        string parameters,
        string returnType,
        string expression)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": {{parameters}},
            "returnType": "{{returnType}}",
            "body": [{ "op": "return", "value": {{expression}} }]
          }]
        }
        """;
}
