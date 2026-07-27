namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterF64PlanSetupModule
{
    public const string Json = """
    {
      "id": "interpreter-f64-plan-setup",
      "version": "1.0.0",
      "functions": [{
        "id": "rawSetup",
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
      }, {
        "id": "literalSetup",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "total", "value": { "f64": 0.0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{ "op": "set", "name": "total", "value": { "f64": 3.25 } }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }, {
        "id": "nested",
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
      }, {
        "id": "intrinsicControl",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "F64",
        "body": [
          { "op": "set", "name": "total", "value": { "f64": 4.0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "call": "math.sqrt", "args": [{ "var": "total" }] }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }, {
        "id": "i64Control",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 1 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "op": "add", "left": { "var": "total" }, "right": { "i64": 3 } }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;
}
