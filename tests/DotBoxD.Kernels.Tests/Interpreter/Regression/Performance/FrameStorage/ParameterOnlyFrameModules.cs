namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class ParameterOnlyFrameModules
{
    public const string ZeroParameters = """
    {
      "id": "parameter-only-frame-zero",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 7 } }]
      }]
    }
    """;

    public const string OneRawParameter = """
    {
      "id": "parameter-only-frame-one",
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

    public const string TwoRawParameters = """
    {
      "id": "parameter-only-frame-two",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "left", "type": "I32" },
          { "name": "right", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "var": "left" } }]
      }]
    }
    """;

    public const string GenuineRawLocal = """
    {
      "id": "parameter-only-frame-local-control",
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

    public const string AssignedRawLocal = """
    {
      "id": "parameter-only-frame-assigned-local",
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
      "id": "parameter-only-frame-unassigned-local",
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

    public const string ParameterOnlyHelper = """
    {
      "id": "parameter-only-helper-frame",
      "version": "1.0.0",
      "functions": [
        {
          "id": "increment",
          "visibility": "private",
          "parameters": [{ "name": "value", "type": "I64" }],
          "returnType": "I64",
          "body": [
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

    public static string Reassignment(string moduleId, string type, string increment)
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
              {
                "op": "set",
                "name": "value",
                "value": { "op": "add", "left": { "var": "value" }, "right": {{increment}} }
              },
              { "op": "return", "value": { "var": "value" } }
            ]
          }]
        }
        """;
}
