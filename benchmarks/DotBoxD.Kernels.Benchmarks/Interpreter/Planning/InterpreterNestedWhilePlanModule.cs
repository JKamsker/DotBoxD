namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterNestedWhilePlanModule
{
    public const string Json = """
    {
      "id": "interpreter-nested-while-plan",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "enterWhile", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          { "op": "set", "name": "guard", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": {
                  "op": "add",
                  "left": { "var": "total" },
                  "right": { "i32": 1 }
                }
              },
              {
                "op": "if",
                "condition": {
                  "op": "ne",
                  "left": { "var": "enterWhile" },
                  "right": { "i32": 0 }
                },
                "then": [{
                  "op": "while",
                  "condition": {
                    "op": "lt",
                    "left": { "var": "guard" },
                    "right": { "i32": 0 }
                  },
                  "body": [{
                    "op": "set",
                    "name": "guard",
                    "value": {
                      "op": "add",
                      "left": { "var": "guard" },
                      "right": { "i32": 1 }
                    }
                  }]
                }],
                "else": []
              }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
