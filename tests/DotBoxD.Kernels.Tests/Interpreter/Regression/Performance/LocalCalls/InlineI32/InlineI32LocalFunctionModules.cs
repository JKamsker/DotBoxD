namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

internal static class InlineI32LocalFunctionModules
{
    public const string CollectionPrecedence = """
    {
      "id": "inline-i32-collection-precedence",
      "version": "1.0.0",
      "functions": [
        {
          "id": "list.count",
          "visibility": "private",
          "parameters": [{ "name": "items", "type": { "name": "List", "arguments": ["I32"] } }],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 42 } }]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [{ "name": "items", "type": { "name": "List", "arguments": ["I32"] } }],
          "returnType": "I32",
          "body": [
            { "op": "set", "name": "result", "value": { "call": "list.count", "args": [{ "var": "items" }] } },
            { "op": "return", "value": { "var": "result" } }
          ]
        }
      ]
    }
    """;

    public const string PendingHelper = """
    {
      "id": "inline-i32-pending-fallback",
      "version": "1.0.0",
      "functions": [
        {
          "id": "step",
          "visibility": "private",
          "parameters": [{ "name": "operand", "type": "I32" }],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "call": "test.pause", "args": [{ "var": "operand" }] } }]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [{ "name": "value", "type": "I32" }],
          "returnType": "I32",
          "body": [
            { "op": "set", "name": "result", "value": { "call": "step", "args": [{ "var": "value" }] } },
            { "op": "return", "value": { "var": "result" } }
          ]
        }
      ]
    }
    """;

    public static string SingleCall(
        string id,
        string helperExpression,
        string argumentExpression = """{ "var": "value" }""")
        => Module(
            id,
            """{ "name": "operand", "type": "I32" }""",
            $$"""[{ "op": "return", "value": {{helperExpression}} }]""",
            argumentExpression);

    public static string Recurrences(string id, int count, bool inlineable)
    {
        var statements = string.Join(
            ",",
            Enumerable.Repeat(
                """{ "op": "set", "name": "value", "value": { "call": "step", "args": [{ "var": "value" }] } }""",
                count));
        var body = inlineable
            ? """{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } }"""
            : """{ "op": "add", "left": { "var": "operand" }, "right": { "var": "operand" } }""";
        return $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "step",
              "visibility": "private",
              "parameters": [{ "name": "operand", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {{body}} }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [{{statements}}{{(count == 0 ? "" : ",")}}{ "op": "return", "value": { "var": "value" } }]
            }
          ]
        }
        """;
    }

    public static string MultiStatementHelper(string id)
        => Module(
            id,
            """{ "name": "operand", "type": "I32" }""",
            """
            [
              { "op": "set", "name": "copy", "value": { "var": "operand" } },
              { "op": "return", "value": { "op": "add", "left": { "var": "copy" }, "right": { "i32": 1 } } }
            ]
            """,
            """{ "var": "value" }""");

    public static string TwoArgumentHelper(string id)
        => Module(
            id,
            """{ "name": "left", "type": "I32" }, { "name": "right", "type": "I32" }""",
            """[{ "op": "return", "value": { "op": "add", "left": { "var": "left" }, "right": { "var": "right" } } }]""",
            """{ "var": "value" }, { "i32": 1 }""");

    public static string SourceRead(string id, bool assignBeforeCall, bool reserveSlot)
    {
        var before = assignBeforeCall
            ? """{ "op": "set", "name": "source", "value": { "var": "value" } },"""
            : "";
        var after = !assignBeforeCall && reserveSlot
            ? """,{ "op": "set", "name": "source", "value": { "var": "value" } }"""
            : "";
        return $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "step",
              "visibility": "private",
              "parameters": [{ "name": "operand", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {{before}}{ "op": "set", "name": "result", "value": { "call": "step", "args": [{ "var": "source" }] } }{{after}},
                { "op": "return", "value": { "var": "result" } }
              ]
            }
          ]
        }
        """;
    }

    private static string Module(
        string id,
        string helperParameters,
        string helperBody,
        string callArguments)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "step",
              "visibility": "private",
              "parameters": [{{helperParameters}}],
              "returnType": "I32",
              "body": {{helperBody}}
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "result", "value": { "call": "step", "args": [{{callArguments}}] } },
                { "op": "return", "value": { "var": "result" } }
              ]
            }
          ]
        }
        """;
}
