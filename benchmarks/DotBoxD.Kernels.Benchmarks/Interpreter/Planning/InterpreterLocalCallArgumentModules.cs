namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterLocalCallArgumentModules
{
    public static string ZeroArity()
        => Module(
            0,
            parametersJson: "",
            argumentsJson: "",
            paddingStatementsJson: """
            { "op": "set", "name": "padding0", "value": { "string": "unused" } },
            { "op": "set", "name": "padding1", "value": { "string": "unused" } },
            { "op": "set", "name": "padding2", "value": { "string": "unused" } }
            """);

    public static string OneArity()
        => Module(
            1,
            parametersJson: """{ "name": "value", "type": "String" }""",
            argumentsJson: """{ "var": "value" }""",
            paddingStatementsJson: """
            { "op": "set", "name": "padding1", "value": { "string": "unused" } },
            { "op": "set", "name": "padding2", "value": { "string": "unused" } }
            """);

    public static string TwoArity()
        => Module(
            2,
            parametersJson: """
            { "name": "left", "type": "String" },
            { "name": "right", "type": "String" }
            """,
            argumentsJson: """
            { "var": "left" },
            { "var": "right" }
            """,
            paddingStatementsJson: """
            { "op": "set", "name": "padding2", "value": { "string": "unused" } }
            """);

    public static string ThreeArity()
        => Module(
            3,
            parametersJson: """
            { "name": "first", "type": "String" },
            { "name": "second", "type": "String" },
            { "name": "third", "type": "String" }
            """,
            argumentsJson: """
            { "var": "first" },
            { "var": "second" },
            { "var": "third" }
            """,
            paddingStatementsJson: "");

    private static string Module(
        int arity,
        string parametersJson,
        string argumentsJson,
        string paddingStatementsJson)
        => $$"""
        {
          "id": "interpreter-local-call-arguments-{{arity}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [{{parametersJson}}],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "bool": false },
                  "then": [{{paddingStatementsJson}}],
                  "else": []
                },
                { "op": "return", "value": { "i32": 7 } }
              ]
            },
            {
              "id": "direct",
              "visibility": "entrypoint",
              "parameters": [{{parametersJson}}],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 7 } }]
            },
            {
              "id": "call",
              "visibility": "entrypoint",
              "parameters": [{{parametersJson}}],
              "returnType": "I32",
              "body": [{
                "op": "return",
                "value": { "call": "helper", "args": [{{argumentsJson}}] }
              }]
            }
          ]
        }
        """;
}
