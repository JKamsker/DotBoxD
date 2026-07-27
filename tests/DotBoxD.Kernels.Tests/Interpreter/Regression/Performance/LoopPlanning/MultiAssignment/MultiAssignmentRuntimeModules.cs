namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

internal static class MultiAssignmentRuntimeModules
{
    public const string OrderedForRange = """
    {
      "id": "i32-multi-assignment-for-ordering",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "iterations", "type": "I32" },
          { "name": "increment", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          { "op": "set", "name": "observed", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "increment" } }
              },
              {
                "op": "set",
                "name": "observed",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "observed" } }
        ]
      }]
    }
    """;

    public const string OrderedThreeForRange = """
    {
      "id": "i32-three-assignment-for-ordering",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "iterations", "type": "I32" },
          { "name": "increment", "type": "I32" },
          { "name": "trailingSource", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          { "op": "set", "name": "intermediate", "value": { "i32": 0 } },
          { "op": "set", "name": "observed", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "increment" } }
              },
              {
                "op": "set",
                "name": "intermediate",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } }
              },
              {
                "op": "set",
                "name": "observed",
                "value": { "op": "add", "left": { "var": "intermediate" }, "right": { "var": "trailingSource" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "observed" } }
        ]
      }]
    }
    """;

    public const string OrderedWhile = """
    {
      "id": "i32-multi-assignment-while-ordering",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "limit", "type": "I32" },
          { "name": "increment", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          { "op": "set", "name": "observed", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [
              {
                "op": "set",
                "name": "counter",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "var": "increment" } }
              },
              {
                "op": "set",
                "name": "observed",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "var": "increment" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "observed" } }
        ]
      }]
    }
    """;

    public const string FaultingForRange = """
    {
      "id": "i32-multi-assignment-for-fault",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "iterations", "type": "I32" },
          { "name": "divisor", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "completed", "value": { "i32": 0 } },
          { "op": "set", "name": "result", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "completed",
                "value": { "op": "add", "left": { "var": "completed" }, "right": { "i32": 1 } }
              },
              {
                "op": "set",
                "name": "result",
                "value": { "op": "div", "left": { "i32": 100 }, "right": { "var": "divisor" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;

    public const string FaultingWhile = """
    {
      "id": "i32-multi-assignment-while-fault",
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
          { "op": "set", "name": "result", "value": { "i32": 0 } },
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
                "name": "result",
                "value": { "op": "div", "left": { "i32": 100 }, "right": { "var": "divisor" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;
}
