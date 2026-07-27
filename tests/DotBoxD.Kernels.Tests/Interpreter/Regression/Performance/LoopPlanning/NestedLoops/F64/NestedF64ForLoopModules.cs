namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops.F64;

internal static class NestedF64ForLoopModules
{
    public const string NonFinite = """
    {
      "id": "nested-f64-nonfinite",
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
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "i32": 2 },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "i32": 1 },
              "body": [{
                "op": "set",
                "name": "result",
                "value": {
                  "op": "mul",
                  "left": { "var": "result" },
                  "right": { "var": "value" }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "result" } }
        ]
      }]
    }
    """;
}
