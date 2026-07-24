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

    public const string MultiSourceOrdered = """
    {
      "id": "i64-cached-multi-source-ordered",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "source", "type": "I64" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "result", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "intermediate",
              "value": { "op": "add", "left": { "var": "source" }, "right": { "i64": 1 } }
            }, {
              "op": "set",
              "name": "result",
              "value": { "op": "add", "left": { "var": "intermediate" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;

    public const string MultiCheckedSecond = """
    {
      "id": "i64-cached-multi-checked-second",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I64" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "first", "value": { "var": "value" } },
          { "op": "set", "name": "result", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 1 },
            "body": [{
              "op": "set",
              "name": "first",
              "value": { "op": "add", "left": { "var": "first" }, "right": { "i64": 1 } }
            }, {
              "op": "set",
              "name": "result",
              "value": { "op": "add", "left": { "var": "first" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;

    public const string MultiQuota = """
    {
      "id": "i64-cached-multi-quota",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I64" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "first", "value": { "var": "value" } },
          { "op": "set", "name": "result", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 3 },
            "body": [{
              "op": "set",
              "name": "first",
              "value": { "op": "add", "left": { "var": "first" }, "right": { "i64": 1 } }
            }, {
              "op": "set",
              "name": "result",
              "value": { "op": "add", "left": { "var": "first" }, "right": { "i64": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;
}
