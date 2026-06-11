using System.Globalization;

namespace SafeIR.Tests;

public sealed class CanonicalModuleHasherTests
{
    [Fact]
    public void Identical_modules_share_canonical_hash()
    {
        var first = SafeIrJsonImporter.Import(SumModule());
        var second = SafeIrJsonImporter.Import(SumModule());

        Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Statement_order_is_semantic()
    {
        var declareThenOther = SafeIrJsonImporter.Import(ModuleWithBody(
            """
            { "op": "set", "name": "first", "value": { "i32": 1 } },
            { "op": "set", "name": "second", "value": { "i32": 2 } },
            { "op": "return", "value": { "var": "first" } }
            """));
        var swapped = SafeIrJsonImporter.Import(ModuleWithBody(
            """
            { "op": "set", "name": "second", "value": { "i32": 2 } },
            { "op": "set", "name": "first", "value": { "i32": 1 } },
            { "op": "return", "value": { "var": "first" } }
            """));

        Assert.NotEqual(CanonicalModuleHasher.Hash(declareThenOther), CanonicalModuleHasher.Hash(swapped));
    }

    [Fact]
    public void Operand_order_is_semantic()
    {
        var left = SafeIrJsonImporter.Import(ModuleWithReturn(
            """{ "op": "sub", "left": { "i32": 1 }, "right": { "i32": 2 } }""",
            "I32"));
        var right = SafeIrJsonImporter.Import(ModuleWithReturn(
            """{ "op": "sub", "left": { "i32": 2 }, "right": { "i32": 1 } }""",
            "I32"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(left), CanonicalModuleHasher.Hash(right));
    }

    [Fact]
    public void Literal_authored_type_affects_hash()
    {
        var asInt = SafeIrJsonImporter.Import(ModuleWithReturn("""{ "i32": 1 }""", "I32"));
        var asDouble = SafeIrJsonImporter.Import(ModuleWithReturn("""{ "f64": 1.0 }""", "F64"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(asInt), CanonicalModuleHasher.Hash(asDouble));
    }

    [Fact]
    public void Parameter_name_is_part_of_hash()
    {
        var first = SafeIrJsonImporter.Import(ModuleWithParameter("p0"));
        var second = SafeIrJsonImporter.Import(ModuleWithParameter("p1"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Floating_point_literals_hash_independently_of_current_culture()
    {
        var module = SafeIrJsonImporter.Import(ModuleWithReturn("""{ "f64": 1.5 }""", "F64"));
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var germanHash = CanonicalModuleHasher.Hash(module);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var englishHash = CanonicalModuleHasher.Hash(module);
            var serialized = CanonicalModuleHasher.Serialize(module);

            Assert.Equal(germanHash, englishHash);
            Assert.Contains("f64:1.5", serialized);
            Assert.DoesNotContain("f64:1,5", serialized);
        }
        finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Canonical_serialization_escapes_internal_separators()
    {
        var module = SafeIrJsonImporter.Import(ModuleWithReturn(
            """{ "string": "a\u001fb\r\nc\\d" }""",
            "String"));

        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Contains("string:a\\u001fb\\r\\nc\\\\d", serialized);
    }

    private static string ModuleWithReturn(string expression, string returnType)
        => $$"""
        {
          "id": "canonical-literals",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

    private static string ModuleWithBody(string body)
        => $$"""
        {
          "id": "canonical-statements",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {{body}}
              ]
            }
          ]
        }
        """;

    private static string ModuleWithParameter(string name)
        => $$"""
        {
          "id": "canonical-parameters",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "{{name}}", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "var": "{{name}}" } }]
            }
          ]
        }
        """;

    private static string SumModule()
        => """
        {
          "id": "canonical-sum",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "n", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "sum", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 1 },
                  "end": { "var": "n" },
                  "body": [
                    {
                      "op": "set",
                      "name": "sum",
                      "value": { "op": "add", "left": { "var": "sum" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "sum" } }
              ]
            }
          ]
        }
        """;
}
