namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterMultiAssignmentPlanModules
{
    public const string ForRange = """
    {
      "id": "interpreter-plan-two-assignments",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          { "op": "set", "name": "doubled", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": {
                  "op": "rem",
                  "left": { "op": "add", "left": { "var": "total" }, "right": { "i32": 3 } },
                  "right": { "i32": 1000003 }
                }
              },
              {
                "op": "set",
                "name": "doubled",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "total" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "doubled" } }
        ]
      }]
    }
    """;

    public const string While = """
    {
      "id": "interpreter-while-two-assignments",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          { "op": "set", "name": "doubled", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [
              {
                "op": "set",
                "name": "counter",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "i32": 1 } }
              },
              {
                "op": "set",
                "name": "doubled",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "var": "counter" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "doubled" } }
        ]
      }]
    }
    """;

    public const string WhileNoLoop = """
    {
      "id": "interpreter-while-two-local-no-loop-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          { "op": "set", "name": "doubled", "value": { "i32": 0 } },
          { "op": "return", "value": { "var": "doubled" } }
        ]
      }]
    }
    """;
}
