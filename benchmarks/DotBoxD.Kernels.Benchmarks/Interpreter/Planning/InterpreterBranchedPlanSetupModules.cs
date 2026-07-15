namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterBranchedPlanSetupModules
{
    public static string OneAssignment(string type)
        => Module(
            $"branched-plan-{type}-single",
            type,
            """
            {
              "op": "if",
              "condition": {
                "op": "lt",
                "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
                "right": { "i32": 1 }
              },
              "then": [{ "op": "set", "name": "total", "value": {
                "op": "add", "left": { "var": "total" }, "right": { "VALUE": 2 }
              } }],
              "else": [{ "op": "set", "name": "total", "value": {
                "op": "add", "left": { "var": "total" }, "right": { "VALUE": 4 }
              } }]
            }
            """);

    public static string NoBranch(string type)
        => Module(
            $"branched-plan-{type}-no-branch",
            type,
            """
            { "op": "set", "name": "total", "value": {
              "op": "add", "left": { "var": "total" }, "right": { "VALUE": 2 }
            } }
            """);

    public static string EmptyBranch(string type)
        => Module(
            $"branched-plan-{type}-empty-branch",
            type,
            """
            {
              "op": "if",
              "condition": {
                "op": "lt",
                "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
                "right": { "i32": 1 }
              },
              "then": [],
              "else": [{ "op": "set", "name": "total", "value": {
                "op": "add", "left": { "var": "total" }, "right": { "VALUE": 4 }
              } }]
            }
            """);

    public static string TwoAssignments(string type)
        => Module(
            $"branched-plan-{type}-two-assignments",
            type,
            """
            {
              "op": "if",
              "condition": {
                "op": "lt",
                "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
                "right": { "i32": 1 }
              },
              "then": [
                { "op": "set", "name": "total", "value": {
                  "op": "add", "left": { "var": "total" }, "right": { "VALUE": 2 }
                } },
                { "op": "set", "name": "doubled", "value": {
                  "op": "add", "left": { "var": "total" }, "right": { "var": "total" }
                } }
              ],
              "else": [
                { "op": "set", "name": "total", "value": {
                  "op": "add", "left": { "var": "total" }, "right": { "VALUE": 4 }
                } },
                { "op": "set", "name": "doubled", "value": {
                  "op": "add", "left": { "var": "total" }, "right": { "var": "total" }
                } }
              ]
            }
            """,
            includeDoubled: true);

    private static string Module(string id, string type, string loopBody, bool includeDoubled = false)
    {
        var valueBody = loopBody.Replace("VALUE", type, StringComparison.Ordinal);
        var doubledInitialization = includeDoubled
            ? $"{{ \"op\": \"set\", \"name\": \"doubled\", \"value\": {{ \"{type}\": 0 }} }},"
            : "";
        var resultName = includeDoubled ? "doubled" : "total";
        return $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "{{type.ToUpperInvariant()}}",
            "body": [
              { "op": "set", "name": "total", "value": { "{{type}}": 1 } },
              {{doubledInitialization}}
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "var": "iterations" },
                "body": [{{valueBody}}]
              },
              { "op": "return", "value": { "var": "{{resultName}}" } }
            ]
          }]
        }
        """;
    }
}
