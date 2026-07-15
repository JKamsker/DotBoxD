namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterAuditEnvelopeModules
{
    public const string PureSuccess = """
    {
      "id": "interpreter-audit-envelope-success",
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
      "id": "interpreter-audit-envelope-failure",
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

    public const string AuditedBinding = """
    {
      "id": "interpreter-audit-envelope-binding",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "audit-envelope probe" }],
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [{
          "op": "return",
          "value": { "call": "log.info", "args": [{ "string": "ok" }] }
        }]
      }]
    }
    """;
}
