namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterNestedLoopPlanModule
{
    public const string Json = """
    {
      "id": "interpreter-nested-loop-plan",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
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
                  "op": "rem",
                  "left": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "i32": 3 }
                  },
                  "right": { "i32": 1000003 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }, {
        "id": "indexSensitive",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
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
                  "op": "rem",
                  "left": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "var": "innerIndex" }
                  },
                  "right": { "i32": 1000003 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }, {
        "id": "unsupportedBound",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": {
                "op": "add",
                "left": { "var": "innerIterations" },
                "right": { "i32": 0 }
              },
              "body": [{
                "op": "set",
                "name": "total",
                "value": {
                  "op": "rem",
                  "left": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "i32": 3 }
                  },
                  "right": { "i32": 1000003 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
