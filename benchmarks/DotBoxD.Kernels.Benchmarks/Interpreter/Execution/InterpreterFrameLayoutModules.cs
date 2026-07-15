namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterFrameLayoutModules
{
    public const string ZeroParameters = """
    {
      "id": "frame-layout-zero-parameter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 1 } }]
      }]
    }
    """;

    public const string OneRawParameter = """
    {
      "id": "frame-layout-parameter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "var": "value" } }]
      }]
    }
    """;

    public const string EightLocalChain = """
    {
      "id": "frame-layout-locals",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "a", "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } } },
          { "op": "set", "name": "b", "value": { "op": "add", "left": { "var": "a" }, "right": { "i32": 2 } } },
          { "op": "set", "name": "c", "value": { "op": "add", "left": { "var": "b" }, "right": { "i32": 3 } } },
          { "op": "set", "name": "d", "value": { "op": "add", "left": { "var": "c" }, "right": { "i32": 4 } } },
          { "op": "set", "name": "e", "value": { "op": "add", "left": { "var": "d" }, "right": { "i32": 5 } } },
          { "op": "set", "name": "f", "value": { "op": "add", "left": { "var": "e" }, "right": { "i32": 6 } } },
          { "op": "set", "name": "g", "value": { "op": "add", "left": { "var": "f" }, "right": { "i32": 7 } } },
          { "op": "set", "name": "h", "value": { "op": "add", "left": { "var": "g" }, "right": { "i32": 8 } } },
          { "op": "return", "value": { "var": "h" } }
        ]
      }]
    }
    """;

    public const string RawParameterAndBoxedLocal = """
    {
      "id": "frame-layout-mixed",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "kept boxed" } },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string GenuineRawLocal = """
    {
      "id": "frame-layout-raw-local-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "value", "value": { "i32": 7 } },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string UnassignedRawLocal = """
    {
      "id": "frame-layout-unassigned-raw-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          {
            "op": "if",
            "condition": { "bool": false },
            "then": [{ "op": "set", "name": "value", "value": { "i32": 7 } }],
            "else": []
          },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string TwoRawParameters = """
    {
      "id": "frame-layout-two-parameter",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "left", "type": "I32" },
          { "name": "right", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "add", "left": { "var": "left" }, "right": { "var": "right" } }
        }]
      }]
    }
    """;
}
