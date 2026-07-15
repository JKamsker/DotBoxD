using System.Text;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

internal static class StraightScalarAssignmentModules
{
    public static string Recurrences(
        string moduleId,
        string type,
        string literalName,
        string increment,
        int count,
        bool useRawStep)
    {
        var parameters = useRawStep
            ? $$"""
              [
                { "name": "value", "type": "{{type}}" },
                { "name": "step", "type": "{{type}}" }
              ]
            """
            : $$"""[{ "name": "value", "type": "{{type}}" }]""";
        var rightOperand = useRawStep
            ? """{ "var": "step" }"""
            : $$"""{ "{{literalName}}": {{increment}} }""";
        var body = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            body.AppendLine($$"""
              {
                "op": "set",
                "name": "value",
                "value": {
                  "op": "add",
                  "left": { "var": "value" },
                  "right": {{rightOperand}}
                }
              },
            """);
        }

        return $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": {{parameters}},
            "returnType": "{{type}}",
            "body": [
              {{body}}
              { "op": "return", "value": { "var": "value" } }
            ]
          }]
        }
        """;
    }

    public static string Assignment(
        string moduleId,
        string inputType,
        string returnType,
        string target,
        string expressionJson)
        => $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "{{inputType}}" }],
            "returnType": "{{returnType}}",
            "body": [
              { "op": "set", "name": "{{target}}", "value": {{expressionJson}} },
              { "op": "return", "value": { "var": "{{target}}" } }
            ]
          }]
        }
        """;

    public static string ReadBeforeAssignment(
        string moduleId,
        string type,
        string literalName,
        bool assignSource)
    {
        var sourceStatement = assignSource
            ? $$"""{ "op": "set", "name": "source", "value": { "{{literalName}}": 1 } },"""
            : $$"""
              {
                "op": "if",
                "condition": { "bool": false },
                "then": [{ "op": "set", "name": "source", "value": { "{{literalName}}": 1 } }],
                "else": []
              },
            """;
        return $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "{{type}}" }],
            "returnType": "{{type}}",
            "body": [
              {{sourceStatement}}
              {
                "op": "set",
                "name": "value",
                "value": {
                  "op": "add",
                  "left": { "var": "source" },
                  "right": { "{{literalName}}": 1 }
                }
              },
              { "op": "return", "value": { "var": "value" } }
            ]
          }]
        }
        """;
    }
}
