namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class F64ForLoopPlanCacheModules
{
    public const string Counter = """
    {
      "id": "f64-cached-loop-counter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "total", "value": { "f64": 1.0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "op": "add", "left": { "var": "total" }, "right": { "f64": 3.0 } }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string NonFiniteMultiply = """
    {
      "id": "f64-cached-loop-nonfinite",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "F64" }],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "result", "value": { "var": "value" } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "result",
              "value": { "op": "mul", "left": { "var": "result" }, "right": { "var": "value" } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;

    public const string Sqrt = """
    {
      "id": "f64-loop-sqrt-fallback",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "F64" }],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "result", "value": { "var": "value" } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "result",
              "value": { "call": "math.sqrt", "args": [{ "var": "result" }] }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;

    public const string Nested = """
    {
      "id": "f64-cached-nested-loop",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "total", "value": { "f64": 0.0 } },
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
                "value": { "op": "add", "left": { "var": "total" }, "right": { "f64": 1.5 } }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
