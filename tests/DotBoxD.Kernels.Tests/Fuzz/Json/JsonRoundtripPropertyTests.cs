using System.Text.Json.Nodes;
using CsCheck;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonRoundtripPropertyTests
{
    [Fact]
    public void Generated_expression_trees_preserve_canonical_identity_through_export()
        => Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            var document = Document(Expression(random, 5));
            var module = JsonImporter.Import(document.ToJsonString());
            var exported = JsonExporter.Export(module);
            var reparsed = JsonImporter.Import(exported);
            Assert.Equal(CanonicalModuleHasher.Hash(module), CanonicalModuleHasher.Hash(reparsed));
            Assert.Equal(exported, JsonExporter.Export(reparsed));
            Assert.Equal(CanonicalModuleHasher.Hash(module),
                CanonicalModuleHasher.Hash(JsonImporter.Import(JsonExporter.Export(module, indented: true))));
        }, seed: "0N0XIzNsQ0O2", iter: 250, threads: 1);

    [Fact]
    public void Mutating_valid_json_never_leaks_parser_exceptions_or_creates_unexportable_modules()
        => Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            var original = Document(Expression(random, 3)).ToJsonString();
            var position = random.Next(original.Length);
            const string insertions = "\"\\\u0000[]{}:";
            var mutated = (seed & 3) switch
            {
                0 => original.Remove(position, 1),
                1 => original.Insert(position, insertions[random.Next(insertions.Length)].ToString()),
                2 => original[..position],
                _ => original[..position] + (char)random.Next(128) + original[(position + 1)..]
            };
            SandboxModule module;
            try
            {
                module = JsonImporter.Import(mutated);
            }
            catch (SandboxValidationException exception)
            {
                Assert.NotEmpty(exception.Diagnostics);
                Assert.All(exception.Diagnostics, diagnostic => Assert.StartsWith("E-JSON-", diagnostic.Code, StringComparison.Ordinal));
                return;
            }

            // Keep the oracle outside the rejection catch: export/reimport failures are bugs.
            var exported = JsonExporter.Export(module);
            Assert.Equal(CanonicalModuleHasher.Hash(module), CanonicalModuleHasher.Hash(JsonImporter.Import(exported)));
        }, seed: "0N0XIzNsQ0O2", iter: 1000, threads: 1);

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("functions")]
    public void Duplicate_root_fields_are_rejected_even_when_values_agree(string field)
    {
        var document = Document(new JsonObject { ["i32"] = 1 });
        var json = document.ToJsonString();
        var duplicate = json.Insert(1, $"\"{field}\":{document[field]!.ToJsonString()},");
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(duplicate));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-SCHEMA");
    }

    private static JsonObject Document(JsonObject expression) => new()
    {
        ["id"] = "roundtrip-fuzz",
        ["version"] = "1.0.0",
        ["metadata"] = new JsonObject { ["unicode"] = "世界🚀\0", ["empty"] = "" },
        ["functions"] = new JsonArray(new JsonObject
        {
            ["id"] = "main",
            ["visibility"] = "entrypoint",
            ["parameters"] = new JsonArray(new JsonObject { ["name"] = "x", ["type"] = "I32" }),
            ["returnType"] = "I32",
            ["body"] = new JsonArray(new JsonObject { ["op"] = "return", ["value"] = expression })
        })
    };

    private static JsonObject Expression(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            return random.Next(2) == 0
                ? new JsonObject { ["i32"] = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1) }
                : new JsonObject { ["var"] = "x" };
        }

        string[] operators = ["add", "sub", "mul", "div", "rem"];
        return new JsonObject
        {
            ["op"] = operators[random.Next(operators.Length)],
            ["left"] = Expression(random, depth - 1),
            ["right"] = Expression(random, depth - 1)
        };
    }
}
