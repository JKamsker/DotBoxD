namespace DotBoxD.Kernels.Benchmarks.Interpreter;

// Extra coverage beyond the core intrinsic/binding matrix. Integration owns the f64/nested/branch/while/i64 cases;
// topic branches can append additional unapproved control-flow probes via All().
internal static class PerformanceMatrixControlFlowCases
{
    public static IReadOnlyList<PerformanceMatrixCase> All()
        => [
            new("while i32 add/rem loop", 2_000_000, 50_000, HandwrittenWhileI32Modulo, WhileI32ModuloJson()),
            new("if branch i32 loop", 2_000_000, 50_000, HandwrittenIfBranch, IfBranchJson()),
            new("two-arg local function", 750_000, 25_000, HandwrittenTwoArgLocalCall, TwoArgLocalCallJson())
        ];

    public static object HandwrittenF64Arithmetic(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            total = (total * 0.9) + 0.1;
        }

        return total;
    }

    public static object HandwrittenNestedLoop(int iterations)
    {
        var acc = 0;
        for (var i = 0; i < iterations; i++)
        {
            for (var j = 0; j < 1000; j++)
            {
                acc = (acc + j) % 1_000_003;
            }
        }

        return acc;
    }

    public static object HandwrittenBranchLoop(int iterations)
    {
        var acc = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (i % 2 < 1)
            {
                acc = (acc + i) % 1_000_003;
            }
            else
            {
                acc = (acc + 1) % 1_000_003;
            }
        }

        return acc;
    }

    public static object HandwrittenI64Arithmetic(int iterations)
    {
        long total = 1;
        for (var i = 0; i < iterations; i++)
        {
            total = ((total * 5) + 7) % 1_000_003;
        }

        return total;
    }

    private static object HandwrittenWhileI32Modulo(int iterations)
    {
        var i = 0;
        var total = 0;
        while (i < iterations)
        {
            total = (total + i) % 1_000_003;
            i++;
        }

        return total;
    }

    public static string I64ArithmeticJson()
        => """
        {
          "id": "matrix-i64-arith",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I64",
              "body": [
                { "op": "set", "name": "total", "value": { "i64": 1 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "op": "mul", "left": { "var": "total" }, "right": { "i64": 5 } }, "right": { "i64": 7 } },
                    "right": { "i64": 1000003 } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    public static object HandwrittenBranchedF64Loop(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            if (i % 2 < 1)
            {
                total = (total * 0.9) + 0.1;
            }
            else
            {
                total = (total * 0.8) + 0.2;
            }
        }

        return total;
    }

    public static string BranchedF64LoopJson()
        => """
        {
          "id": "matrix-branched-f64",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 1.0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "if",
                      "condition": { "op": "lt", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 1 } },
                      "then": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "op": "mul", "left": { "var": "total" }, "right": { "f64": 0.9 } }, "right": { "f64": 0.1 } } }],
                      "else": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "op": "mul", "left": { "var": "total" }, "right": { "f64": 0.8 } }, "right": { "f64": 0.2 } } }] }
                  ] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    public static object HandwrittenWhileLoop(int iterations)
    {
        var acc = 0;
        var i = 0;
        while (i < iterations)
        {
            acc = (acc + i) % 1_000_003;
            i = i + 1;
        }

        return acc;
    }

    public static string WhileLoopJson()
        => """
        {
          "id": "matrix-while-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "i32": 0 } },
                { "op": "set", "name": "i", "value": { "i32": 0 } },
                { "op": "while", "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                  "body": [
                    { "op": "set", "name": "acc", "value": { "op": "rem", "left": { "op": "add", "left": { "var": "acc" }, "right": { "var": "i" } }, "right": { "i32": 1000003 } } },
                    { "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 1 } } }
                  ] },
                { "op": "return", "value": { "var": "acc" } }
              ]
            }
          ]
        }
        """;

    private static object HandwrittenIfBranch(int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total += i % 2 == 0 ? 1 : 2;
        }

        return total;
    }

    private static object HandwrittenTwoArgLocalCall(int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total = Add(total, i % 3);
        }

        return total;
    }

    private static int Add(int left, int right) => left + right;

    public static string F64ArithmeticJson()
        => """
        {
          "id": "matrix-f64-arith",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 1.0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": {
                    "op": "add",
                    "left": { "op": "mul", "left": { "var": "total" }, "right": { "f64": 0.9 } },
                    "right": { "f64": 0.1 } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    public static string NestedLoopJson()
        => """
        {
          "id": "matrix-nested-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "forRange", "local": "j", "start": { "i32": 0 }, "end": { "i32": 1000 },
                      "body": [{ "op": "set", "name": "acc", "value": {
                        "op": "rem",
                        "left": { "op": "add", "left": { "var": "acc" }, "right": { "var": "j" } },
                        "right": { "i32": 1000003 } } }] }
                  ] },
                { "op": "return", "value": { "var": "acc" } }
              ]
            }
          ]
        }
        """;

    public static string BranchLoopJson()
        => """
        {
          "id": "matrix-branch-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "if",
                      "condition": { "op": "lt", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 1 } },
                      "then": [{ "op": "set", "name": "acc", "value": {
                        "op": "rem",
                        "left": { "op": "add", "left": { "var": "acc" }, "right": { "var": "i" } },
                        "right": { "i32": 1000003 } } }],
                      "else": [{ "op": "set", "name": "acc", "value": {
                        "op": "rem",
                        "left": { "op": "add", "left": { "var": "acc" }, "right": { "i32": 1 } },
                        "right": { "i32": 1000003 } } }] }
                  ] },
                { "op": "return", "value": { "var": "acc" } }
              ]
            }
          ]
        }
        """;

    private static string WhileI32ModuloJson()
        => """
        {
          "id": "matrix-while-i32-modulo",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "i", "value": { "i32": 0 } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "while",
                  "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                  "body": [
                    { "op": "set", "name": "total", "value": {
                      "op": "rem",
                      "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                      "right": { "i32": 1000003 } } },
                    { "op": "set", "name": "i", "value": {
                      "op": "add",
                      "left": { "var": "i" },
                      "right": { "i32": 1 } } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string IfBranchJson()
        => """
        {
          "id": "matrix-if-branch",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "if",
                      "condition": { "op": "eq", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 0 } },
                      "then": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } } }],
                      "else": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 2 } } }] }
                  ] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string TwoArgLocalCallJson()
        => """
        {
          "id": "matrix-two-arg-local-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "add",
              "visibility": "private",
              "parameters": [
                { "name": "left", "type": "I32" },
                { "name": "right", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "op": "add", "left": { "var": "left" }, "right": { "var": "right" } } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": {
                    "call": "add",
                    "args": [{ "var": "total" }, { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }] } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
