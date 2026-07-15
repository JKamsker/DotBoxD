namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class MixedFrameAssignmentModules
{
    public const string BoxedParameterAndBoxedLocal = """
    {
      "id": "mixed-frame-boxed-baseline",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "flag", "type": "Bool" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          { "op": "return", "value": { "i32": 7 } }
        ]
      }]
    }
    """;

    public const string RawParameterBoxedLocalAndRawLocal = """
    {
      "id": "mixed-frame-genuine-raw-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          { "op": "set", "name": "rawLocal", "value": { "i32": 11 } },
          { "op": "return", "value": { "i32": 7 } }
        ]
      }]
    }
    """;

    public const string AssignedBoxedLocal = """
    {
      "id": "mixed-frame-assigned-boxed-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          { "op": "return", "value": { "call": "string.length", "args": [{ "var": "label" }] } }
        ]
      }]
    }
    """;

    public const string SkippedBoxedLocal = """
    {
      "id": "mixed-frame-skipped-boxed-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          {
            "op": "if",
            "condition": { "bool": false },
            "then": [{ "op": "set", "name": "label", "value": { "string": "boxed" } }],
            "else": []
          },
          { "op": "return", "value": { "call": "string.length", "args": [{ "var": "label" }] } }
        ]
      }]
    }
    """;

    public const string AssignedRawLoopLocal = """
    {
      "id": "mixed-frame-assigned-raw-loop-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          { "op": "set", "name": "value", "value": { "i32": 7 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 0 },
            "body": []
          },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string SkippedRawLoopLocal = """
    {
      "id": "mixed-frame-skipped-raw-loop-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "i32": 0 },
            "body": [{ "op": "set", "name": "value", "value": { "i32": 7 } }]
          },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string MixedRawParameterHelper = """
    {
      "id": "mixed-frame-raw-parameter-helper",
      "version": "1.0.0",
      "functions": [
        {
          "id": "increment",
          "visibility": "private",
          "parameters": [{ "name": "value", "type": "I64" }],
          "returnType": "I64",
          "body": [
            { "op": "set", "name": "label", "value": { "string": "boxed" } },
            {
              "op": "set",
              "name": "value",
              "value": { "op": "add", "left": { "var": "value" }, "right": { "i64": 2 } }
            },
            { "op": "return", "value": { "var": "value" } }
          ]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [{ "name": "value", "type": "I64" }],
          "returnType": "I64",
          "body": [{ "op": "return", "value": { "call": "increment", "args": [{ "var": "value" }] } }]
        }
      ]
    }
    """;

    public const string PendingBoxedLocal = """
    {
      "id": "mixed-frame-pending-boxed-local",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I64" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "label", "value": { "call": "test.delayedLabel", "args": [] } },
          { "op": "return", "value": { "var": "value" } }
        ]
      }]
    }
    """;

    public const string RawAndBoxedResult = """
    {
      "id": "mixed-frame-raw-and-boxed-result",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "label", "value": { "string": "boxed" } },
          {
            "op": "return",
            "value": {
              "op": "add",
              "left": { "var": "value" },
              "right": { "call": "string.length", "args": [{ "var": "label" }] }
            }
          }
        ]
      }]
    }
    """;

    public static string RawParameterAndBoxedLocal(string moduleId, string type)
        => $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "{{type}}" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "label", "value": { "string": "boxed" } },
              { "op": "return", "value": { "i32": 7 } }
            ]
          }]
        }
        """;

    public static string ReassignedRawParameter(
        string moduleId,
        string type,
        string incrementJson)
        => $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "{{type}}" }],
            "returnType": "{{type}}",
            "body": [
              { "op": "set", "name": "label", "value": { "string": "boxed" } },
              {
                "op": "set",
                "name": "value",
                "value": { "op": "add", "left": { "var": "value" }, "right": {{incrementJson}} }
              },
              { "op": "return", "value": { "var": "value" } }
            ]
          }]
        }
        """;
}
