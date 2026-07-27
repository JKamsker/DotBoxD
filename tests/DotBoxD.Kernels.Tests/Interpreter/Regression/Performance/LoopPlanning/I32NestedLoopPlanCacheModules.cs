namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class I32NestedLoopPlanCacheModules
{
    public const string NestedLoop = """
    {
      "id": "i32-nested-loop-plan-cache",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "innerIterations" },
              "body": [{
                "op": "set",
                "name": "total",
                "value": {
                  "op": "rem",
                  "left": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "i32": 3 }
                  },
                  "right": { "i32": 1000003 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string AlternatingNestedLoops = """
    {
      "id": "i32-alternating-nested-loop-plan-cache",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [
              {
                "op": "forRange",
                "local": "firstInnerIndex",
                "start": { "i32": 0 },
                "end": { "var": "innerIterations" },
                "body": [{
                  "op": "set",
                  "name": "total",
                  "value": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "i32": 1 }
                  }
                }]
              },
              {
                "op": "forRange",
                "local": "secondInnerIndex",
                "start": { "i32": 0 },
                "end": { "var": "innerIterations" },
                "body": [{
                  "op": "set",
                  "name": "total",
                  "value": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "i32": 2 }
                  }
                }]
              }
            ]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string MutatingInnerBound = """
    {
      "id": "i32-nested-loop-mutating-bound",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "remaining", "value": { "var": "innerIterations" } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "remaining" },
              "body": [{
                "op": "set",
                "name": "remaining",
                "value": {
                  "op": "sub",
                  "left": { "var": "remaining" },
                  "right": { "i32": 1 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "remaining" } }
        ]
      }]
    }
    """;

    public const string FaultingInnerAssignment = """
    {
      "id": "i32-nested-loop-faulting-assignment",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "divisor", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "i32": 1 },
              "body": [{
                "op": "set",
                "name": "total",
                "value": {
                  "op": "div",
                  "left": { "i32": 1 },
                  "right": { "var": "divisor" }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string OuterIndexDependent = """
    {
      "id": "i32-nested-loop-outer-index-dependent",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "unused", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "total", "value": { "i32": 0 } },
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "outerIndex" },
              "body": [{
                "op": "set",
                "name": "total",
                "value": {
                  "op": "rem",
                  "left": {
                    "op": "add",
                    "left": { "var": "total" },
                    "right": { "var": "outerIndex" }
                  },
                  "right": { "i32": 1000003 }
                }
              }]
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    public const string MutatingOuterBound = """
    {
      "id": "i32-nested-loop-mutating-outer-bound",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [
          { "name": "outerIterations", "type": "I32" },
          { "name": "innerIterations", "type": "I32" }
        ],
        "returnType": "I32",
        "body": [
          {
            "op": "forRange",
            "local": "outerIndex",
            "start": { "i32": 0 },
            "end": { "var": "outerIterations" },
            "body": [{
              "op": "forRange",
              "local": "innerIndex",
              "start": { "i32": 0 },
              "end": { "var": "innerIterations" },
              "body": [{
                "op": "set",
                "name": "outerIterations",
                "value": { "i32": 0 }
              }]
            }]
          },
          { "op": "return", "value": { "var": "outerIterations" } }
        ]
      }]
    }
    """;
}
