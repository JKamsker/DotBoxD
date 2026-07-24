using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledReturnValidationScenarios
{
    private const string ListI32Type = """{ "name": "List", "arguments": ["I32"] }""";
    private const string MapStringI32Type = """{ "name": "Map", "arguments": ["String", "I32"] }""";
    private const string RecordType = """{ "name": "Record", "arguments": ["I32", "String"] }""";
    private const string NestedType = """
    {
      "name": "Record",
      "arguments": [
        { "name": "Map", "arguments": ["String", "I32"] },
        {
          "name": "List",
          "arguments": [{
            "name": "Record",
            "arguments": [
              "I32",
              "String",
              { "name": "Map", "arguments": ["String", "I64"] }
            ]
          }]
        }
      ]
    }
    """;

    public static IReadOnlyList<CompiledReturnValidationScenario> Create()
    {
        var i32 = SandboxValue.FromInt32(42);
        var emptyList = SandboxValue.FromList([], SandboxType.I32);
        var list32 = I32List(32);
        var list255 = I32List(255);
        var list256 = I32List(256);
        var list1024 = I32List(1_024);
        var record = SandboxValue.FromRecord([
            SandboxValue.FromInt32(7),
            SandboxValue.FromString("record")
        ]);
        var map = StringI32Map(32);
        var nestedType = CreateNestedType();
        var nested = CreateNestedValue();

        return [
            new("unit no-op", UnitModule, SandboxValue.Unit, SandboxType.Unit, 100_000, 5_000_000, 1),
            new("i32 identity", IdentityModule("i32", "\"I32\""), i32, SandboxType.I32, 100_000, 5_000_000, 42),
            new("empty List<I32>", IdentityModule("empty-list", ListI32Type), emptyList,
                SandboxType.List(SandboxType.I32), 100_000, 500_000, 1),
            new("Record<I32,String>", IdentityModule("record", RecordType), record,
                SandboxType.Record([SandboxType.I32, SandboxType.String]), 80_000, 300_000, 2),
            new("List<I32> x32", IdentityModule("list-32", ListI32Type), list32,
                SandboxType.List(SandboxType.I32), 30_000, 100_000, 32),
            new("List<I32> x255", IdentityModule("list-255", ListI32Type), list255,
                SandboxType.List(SandboxType.I32), 10_000, 20_000, 255),
            new("List<I32> x256", IdentityModule("list-256", ListI32Type), list256,
                SandboxType.List(SandboxType.I32), 10_000, 20_000, 256),
            new("List<I32> x1024", IdentityModule("list-1024", ListI32Type), list1024,
                SandboxType.List(SandboxType.I32), 2_500, 5_000, 1_024),
            new("Map<String,I32> x32", IdentityModule("map-32", MapStringI32Type), map,
                SandboxType.Map(SandboxType.String, SandboxType.I32), 20_000, 50_000, 32),
            new("nested record", IdentityModule("nested", NestedType), nested, nestedType, 50_000, 200_000, 13)
        ];
    }

    private const string UnitModule = """
    {
      "id": "compiled-return-validation-unit",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [{ "op": "return", "value": { "unit": true } }]
      }]
    }
    """;

    private static string IdentityModule(string id, string type)
        => $$"""
        {
          "id": "compiled-return-validation-{{id}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": {{type}} }],
            "returnType": {{type}},
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """;

    private static SandboxValue I32List(int count)
    {
        var values = new SandboxValue[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = SandboxValue.FromInt32(i);
        }

        return SandboxValue.FromList(values, SandboxType.I32);
    }

    private static SandboxValue StringI32Map(int count)
    {
        var values = new Dictionary<SandboxValue, SandboxValue>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(SandboxValue.FromString($"key-{i}"), SandboxValue.FromInt32(i));
        }

        return SandboxValue.FromMap(values, SandboxType.String, SandboxType.I32);
    }

    private static SandboxType CreateNestedType()
        => SandboxType.Record([
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            SandboxType.List(SandboxType.Record([
                SandboxType.I32,
                SandboxType.String,
                SandboxType.Map(SandboxType.String, SandboxType.I64)
            ]))
        ]);

    private static SandboxValue CreateNestedValue()
        => SandboxValue.FromRecord([
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1),
                    [SandboxValue.FromString("two")] = SandboxValue.FromInt32(2)
                },
                SandboxType.String,
                SandboxType.I32),
            SandboxValue.FromList(
                [SandboxValue.FromRecord([
                    SandboxValue.FromInt32(7),
                    SandboxValue.FromString("nested"),
                    SandboxValue.FromMap(
                        new Dictionary<SandboxValue, SandboxValue>
                        {
                            [SandboxValue.FromString("score")] = SandboxValue.FromInt64(42)
                        },
                        SandboxType.String,
                        SandboxType.I64)
                ])],
                SandboxType.Record([
                    SandboxType.I32,
                    SandboxType.String,
                    SandboxType.Map(SandboxType.String, SandboxType.I64)
                ]))
        ]);
}

internal sealed record CompiledReturnValidationScenario(
    string Name,
    string ModuleJson,
    SandboxValue Input,
    SandboxType ReturnType,
    int CompiledIterations,
    int ValidationIterations,
    long ChecksumContribution);
