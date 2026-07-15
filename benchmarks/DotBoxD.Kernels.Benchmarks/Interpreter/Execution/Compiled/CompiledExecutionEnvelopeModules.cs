namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionEnvelopeModules
{
    public const string PureSuccess = """
    {
      "id": "compiled-execution-envelope-success",
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

    public const string PureFailure = """
    {
      "id": "compiled-execution-envelope-failure",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }
        }]
      }]
    }
    """;
}
