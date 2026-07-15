namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls;

internal static class LocalFunctionScalarCallModules
{
    public const string OrderedArguments = """
    {
      "id": "local-call-scalar-argument-order",
      "version": "1.0.0",
      "functions": [
        {
          "id": "helper",
          "visibility": "private",
          "parameters": [
            { "name": "first", "type": "I32" },
            { "name": "second", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [{
            "op": "return",
            "value": {
              "call": "test.observeBody",
              "args": [{ "var": "first" }, { "var": "second" }]
            }
          }]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{
            "op": "return",
            "value": {
              "call": "helper",
              "args": [
                { "call": "test.first", "args": [] },
                { "call": "test.second", "args": [] }
              ]
            }
          }]
        }
      ]
    }
    """;

    public const string ConcurrentPair = """
    {
      "id": "local-call-scalar-concurrent",
      "version": "1.0.0",
      "functions": [
        {
          "id": "combine",
          "visibility": "private",
          "parameters": [
            { "name": "left", "type": "I64" },
            { "name": "right", "type": "I64" }
          ],
          "returnType": "I64",
          "body": [{
            "op": "return",
            "value": {
              "op": "add",
              "left": {
                "op": "mul",
                "left": { "var": "left" },
                "right": { "i64": 100000 }
              },
              "right": { "var": "right" }
            }
          }]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [
            { "name": "left", "type": "I64" },
            { "name": "right", "type": "I64" }
          ],
          "returnType": "I64",
          "body": [{
            "op": "return",
            "value": {
              "call": "combine",
              "args": [{ "var": "left" }, { "var": "right" }]
            }
          }]
        }
      ]
    }
    """;

    public const string CollectionNamePrecedence = """
    {
      "id": "local-call-scalar-collection-precedence",
      "version": "1.0.0",
      "functions": [
        {
          "id": "list.of",
          "visibility": "private",
          "parameters": [{ "name": "value", "type": "I32" }],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "var": "value" } }]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": { "name": "List", "arguments": ["I32"] },
          "body": [{
            "op": "return",
            "value": { "call": "list.of", "args": [{ "i32": 7 }] }
          }]
        }
      ]
    }
    """;

    public static string Allocation(int arity)
    {
        var scenario = AllocationScenario.For(arity);
        return $$"""
        {
          "id": "local-call-scalar-allocation-{{arity}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [{{scenario.Parameters}}],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "bool": false },
                  "then": [{{scenario.PaddingStatements}}],
                  "else": []
                },
                { "op": "return", "value": { "i32": 7 } }
              ]
            },
            {
              "id": "direct",
              "visibility": "entrypoint",
              "parameters": [{{scenario.Parameters}}],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 7 } }]
            },
            {
              "id": "call",
              "visibility": "entrypoint",
              "parameters": [{{scenario.Parameters}}],
              "returnType": "I32",
              "body": [{
                "op": "return",
                "value": { "call": "helper", "args": [{{scenario.Arguments}}] }
              }]
            }
          ]
        }
        """;
    }

    public static string Values(int arity)
    {
        var scenario = ValueScenario.For(arity);
        return $$"""
        {
          "id": "local-call-scalar-values-{{arity}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [{{scenario.Parameters}}],
              "returnType": "I64",
              "body": [{ "op": "return", "value": {{scenario.ReturnExpression}} }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [{
                "op": "return",
                "value": { "call": "helper", "args": [{{scenario.Arguments}}] }
              }]
            }
          ]
        }
        """;
    }

    private sealed record AllocationScenario(string Parameters, string Arguments, string PaddingStatements)
    {
        public static AllocationScenario For(int arity)
            => arity switch
            {
                0 => new("", "", Padding(0, 1, 2)),
                1 => new(Parameter("value"), Argument("value"), Padding(1, 2)),
                2 => new(
                    $"{Parameter("left")}, {Parameter("right")}",
                    $"{Argument("left")}, {Argument("right")}",
                    Padding(2)),
                3 => new(
                    $"{Parameter("first")}, {Parameter("second")}, {Parameter("third")}",
                    $"{Argument("first")}, {Argument("second")}, {Argument("third")}",
                    ""),
                _ => throw new ArgumentOutOfRangeException(nameof(arity))
            };

        private static string Parameter(string name)
            => $$"""{ "name": "{{name}}", "type": "String" }""";

        private static string Argument(string name)
            => $$"""{ "var": "{{name}}" }""";

        private static string Padding(params int[] indexes)
            => string.Join(",", indexes.Select(
                index => $$"""{ "op": "set", "name": "padding{{index}}", "value": { "string": "unused" } }"""));
    }

    private sealed record ValueScenario(string Parameters, string Arguments, string ReturnExpression)
    {
        public static ValueScenario For(int arity)
            => arity switch
            {
                0 => new("", "", """{ "i64": 7 }"""),
                1 => new(
                    """{ "name": "value", "type": "I64" }""",
                    """{ "i64": 11 }""",
                    """{ "var": "value" }"""),
                2 => new(
                    """{ "name": "left", "type": "I64" }, { "name": "right", "type": "I64" }""",
                    """{ "i64": 5 }, { "i64": 8 }""",
                    """{ "op": "add", "left": { "var": "left" }, "right": { "var": "right" } }"""),
                3 => new(
                    """
                    { "name": "first", "type": "I64" },
                    { "name": "second", "type": "I64" },
                    { "name": "third", "type": "I64" }
                    """,
                    """{ "i64": 2 }, { "i64": 3 }, { "i64": 4 }""",
                    """
                    {
                      "op": "add",
                      "left": { "op": "add", "left": { "var": "first" }, "right": { "var": "second" } },
                      "right": { "var": "third" }
                    }
                    """),
                _ => throw new ArgumentOutOfRangeException(nameof(arity))
            };
    }
}
