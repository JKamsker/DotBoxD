namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class I64ForLoopPlanCacheModules
{
    public const string Counter = """
    {
      "id": "i64-cached-loop-counter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "op": "add", "left": { "var": "total" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string CheckedAddition = """
    {
      "id": "i64-cached-loop-checked-addition",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I64" }],
        "returnType": "I64",
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
              "value": { "op": "add", "left": { "var": "result" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;
}
