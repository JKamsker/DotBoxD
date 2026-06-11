using System.Globalization;

namespace SafeIR.Tests;

public sealed class CanonicalModuleHasherTests
{
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
            Assert.Contains("1.5", serialized);
            Assert.DoesNotContain("1,5", serialized);
        }
        finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Canonical_serialization_escapes_internal_control_separators()
    {
        var module = SafeIrJsonImporter.Import(ModuleWithReturn(
            """{ "string": "a\u001fb\r\nc\\d" }""",
            "String"));

        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Contains("u001f", serialized);
        Assert.DoesNotContain(serialized, c => c == (char)0x1f);
        Assert.DoesNotContain("\r", serialized);
    }

    [Fact]
    public void Canonical_hash_distinguishes_delimiter_heavy_expression_shapes()
    {
        var first = ModuleWithVariableAdd("a),var(b", "c");
        var second = ModuleWithVariableAdd("a", "b),var(c");

        Assert.NotEqual(CanonicalModuleHasher.Serialize(first), CanonicalModuleHasher.Serialize(second));
        Assert.NotEqual(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
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

    private static SandboxModule ModuleWithVariableAdd(string left, string right)
        => new(
            "canonical-expressions",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [
                        new ReturnStatement(
                            new BinaryExpression(
                                new VariableExpression(left, new SourceSpan(0, 0)),
                                "+",
                                new VariableExpression(right, new SourceSpan(0, 0)),
                                new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());
}
