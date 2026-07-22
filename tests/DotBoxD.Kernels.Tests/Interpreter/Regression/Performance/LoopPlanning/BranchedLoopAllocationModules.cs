namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

internal static class BranchedLoopAllocationModules
{
    public static string OneAssignment(string type)
        => Module(
            $"branched-allocation-{type}-single",
            type,
            Branch(
                ThenAssignment(type, value: 2),
                ThenAssignment(type, value: 4)));

    public static string EmptyBranch(string type)
        => Module(
            $"branched-allocation-{type}-empty",
            type,
            Branch("", ThenAssignment(type, value: 4)));

    public static string NoBranch(string type)
        => Module(
            $"branched-allocation-{type}-control",
            type,
            ThenAssignment(type, value: 2));

    public static string TwoAssignments(string type)
        => Module(
            $"branched-allocation-{type}-multiple",
            type,
            Branch(
                ThenAssignment(type, value: 2) + "," + DoubledAssignment,
                ThenAssignment(type, value: 4) + "," + DoubledAssignment),
            includeDoubled: true);

    public static string UnsupportedElse(string type)
        => Module(
            $"branched-allocation-{type}-unsupported",
            type,
            Branch(ThenAssignment(type, value: 2), "{ \"op\": \"break\" }"));

    public static string InputDependent()
        => Module(
            "branched-allocation-i32-live-input",
            "i32",
            Branch(InputAssignment, InputAssignment));

    public static string FaultingAssignment()
        => Module(
            "branched-allocation-i32-fault",
            "i32",
            Branch(DividingAssignment, DividingAssignment));

    private static string Branch(string thenStatements, string elseStatements)
        => $$"""
        {
          "op": "if",
          "condition": {
            "op": "lt",
            "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
            "right": { "i32": 1 }
          },
          "then": [{{thenStatements}}],
          "else": [{{elseStatements}}]
        }
        """;

    private static string ThenAssignment(string type, int value)
        => $$"""
        { "op": "set", "name": "total", "value": {
          "op": "add", "left": { "var": "total" }, "right": { "{{type}}": {{value}} }
        } }
        """;

    private static string Module(
        string id,
        string type,
        string loopBody,
        bool includeDoubled = false)
    {
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
                "body": [{{loopBody}}]
              },
              { "op": "return", "value": { "var": "{{resultName}}" } }
            ]
          }]
        }
        """;
    }

    private const string DoubledAssignment = """
        { "op": "set", "name": "doubled", "value": {
          "op": "add", "left": { "var": "total" }, "right": { "var": "total" }
        } }
        """;

    private const string InputAssignment = """
        { "op": "set", "name": "total", "value": {
          "op": "add", "left": { "var": "total" }, "right": { "var": "iterations" }
        } }
        """;

    private const string DividingAssignment = """
        { "op": "set", "name": "total", "value": {
          "op": "div",
          "left": { "i32": 1 },
          "right": {
            "op": "sub", "left": { "var": "iterations" }, "right": { "i32": 1 }
          }
        } }
        """;
}
