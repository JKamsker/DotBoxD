namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class I32WhileLoopPlanCacheModules
{
    public const string Counter = """
    {
      "id": "i32-while-plan-counter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [{
              "op": "set",
              "name": "counter",
              "value": { "op": "add", "left": { "var": "counter" }, "right": { "i32": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "counter" } }
        ]
      }]
    }
    """;

    public const string FaultOrdering = """
    {
      "id": "i32-while-plan-fault-ordering",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "limit", "type": "I32" },
          { "name": "divisor", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [{
              "op": "set",
              "name": "counter",
              "value": { "op": "div", "left": { "i32": 1 }, "right": { "var": "divisor" } }
            }]
          },
          { "op": "return", "value": { "var": "counter" } }
        ]
      }]
    }
    """;

    public const string NestedEntry = """
    {
      "id": "i32-nested-while-plan-entry",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "outerIterations", "type": "I32" }],
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
                "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } }
              },
              {
                "op": "while",
                "condition": { "op": "lt", "left": { "var": "guard" }, "right": { "i32": 0 } },
                "body": [{
                  "op": "set",
                  "name": "guard",
                  "value": { "op": "add", "left": { "var": "guard" }, "right": { "i32": 1 } }
                }]
              }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
