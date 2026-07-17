namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls;

internal static class LocalFunctionTripleCallModules
{
    public const string PendingBody = """
    {
      "id": "local-call-triple-pending-body",
      "version": "1.0.0",
      "functions": [
        {
          "id": "helper",
          "visibility": "private",
          "parameters": [
            { "name": "first", "type": "I32" },
            { "name": "second", "type": "I32" },
            { "name": "third", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [
            { "op": "expr", "value": { "call": "test.pause", "args": [] } },
            {
              "op": "return",
              "value": {
                "op": "add",
                "left": {
                  "op": "add",
                  "left": {
                    "op": "mul",
                    "left": { "var": "first" },
                    "right": { "i32": 100 }
                  },
                  "right": {
                    "op": "mul",
                    "left": { "var": "second" },
                    "right": { "i32": 10 }
                  }
                },
                "right": { "var": "third" }
              }
            }
          ]
        },
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{
            "op": "return",
            "value": {
              "call": "helper",
              "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
            }
          }]
        }
      ]
    }
    """;
}
