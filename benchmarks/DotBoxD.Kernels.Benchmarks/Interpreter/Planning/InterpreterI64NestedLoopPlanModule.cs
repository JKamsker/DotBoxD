namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterI64NestedLoopPlanModule
{
    public const string Json = """
    {
      "id": "interpreter-i64-nested-loop-plan",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "innerIterations" },
              "body": [{
                "op": "set",
                "name": "total",
                "value": {
                  "op": "add",
                  "left": { "var": "total" },
                  "right": { "i64": 1 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }, {
        "id": "multiBodyControl",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          { "op": "set", "name": "doubled", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "innerIterations" },
              "body": [{
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "i64": 1 } }
              }, {
                "op": "set",
                "name": "doubled",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "total" } }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
