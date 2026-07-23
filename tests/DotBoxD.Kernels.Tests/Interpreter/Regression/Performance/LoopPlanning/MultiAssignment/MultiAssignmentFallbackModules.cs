namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

internal static class MultiAssignmentFallbackModules
{
    public const string ForRange = """
    {
      "id": "i32-multi-assignment-for-break-fallback",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } }
              },
              { "op": "break" }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string While = """
    {
      "id": "i32-multi-assignment-while-break-fallback",
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
            "body": [
              {
                "op": "set",
                "name": "counter",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "i32": 1 } }
              },
              { "op": "break" }
            ]
          },
          { "op": "return", "value": { "var": "counter" } }
        ]
      }]
    }
    """;
}
