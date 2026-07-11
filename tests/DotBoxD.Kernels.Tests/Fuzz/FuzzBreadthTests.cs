using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsCheck;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Fuzz;

public sealed class FuzzBreadthTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Names = ["a", "b", "c", "value"];

    [Fact]
    public void Json_importer_fuzz_rejects_malformed_expression_shapes_with_diagnostics()
    {
        Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            var i = seed;
            var json = ModuleJson(i, InvalidExpression(random).ToJsonString(JsonOptions));

            var ex = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));

            Assert.NotEmpty(ex.Diagnostics);
            Assert.All(ex.Diagnostics, d => Assert.StartsWith("E-JSON-", d.Code, StringComparison.Ordinal));
        }, seed: "0N0XIzNsQ0O2", iter: 60, threads: 1);
    }

    [Fact]
    public void Canonical_hash_fuzz_is_stable_across_json_property_order()
    {
        Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            var i = seed;
            var expression = ValidExpression(random, depth: 4).ToJsonString(JsonOptions);
            var first = JsonImporter.Import(ModuleJson(i, expression, shuffled: false));
            var second = JsonImporter.Import(ModuleJson(i, expression, shuffled: true));

            Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
        }, seed: "0N0XIzNsQ0O2", iter: 40, threads: 1);
    }

    [Fact]
    public void Policy_hash_fuzz_distinguishes_parameter_and_limit_values()
        => Gen.Int.Sample(seed =>
        {
            var random = new Random(seed);
            var tenant = Token(random);
            var baseline = PolicyHash(tenant, limit: seed, fuel: 1_000);

            Assert.NotEqual(baseline, PolicyHash(tenant + "-changed", limit: seed, fuel: 1_000));
            Assert.NotEqual(baseline, PolicyHash(tenant, limit: seed + 1L, fuel: 1_000));
            Assert.NotEqual(baseline, PolicyHash(tenant, limit: seed, fuel: 1_001));
        }, seed: "0N0XIzNsQ0O2", iter: 40, threads: 1);

    [Fact]
    public void Policy_hash_is_stable_across_grant_parameter_order()
    {
        var first = SandboxPolicyBuilder.Create()
            .Grant("fuzz.cap", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }, SandboxEffect.Cpu)
            .Build();
        var second = SandboxPolicyBuilder.Create()
            .Grant("fuzz.cap", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, SandboxEffect.Cpu)
            .Build();

        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public void Verifier_fuzz_reports_diagnostics_for_malformed_bytes()
    {
        var verifier = new GeneratedAssemblyVerifier();
        var policy = VerificationPolicy.BoxedValueDefaults();
        Gen.Byte.Array[1, 255].Sample(bytes =>
        {
            var result = verifier.VerifyAsync(bytes, Manifest(bytes), policy, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Diagnostics);
        }, seed: "0N0XIzNsQ0O2", iter: 30, threads: 1);
    }

    private static JsonObject InvalidExpression(Random random)
        => random.Next(7) switch
        {
            0 => new JsonObject { ["var"] = 42 },
            1 => new JsonObject { ["i32"] = 1, ["bool"] = true },
            2 => new JsonObject { ["call"] = "math.abs", ["args"] = new JsonObject { ["i32"] = 1 } },
            3 => new JsonObject { ["op"] = "add", ["left"] = new JsonObject { ["i32"] = 1 } },
            4 => new JsonObject { ["path"] = "../secret.txt" },
            5 => new JsonObject { ["uri"] = "not-a-uri" },
            _ => new JsonObject { ["unary"] = "not", ["operand"] = new JsonArray() }
        };

    private static JsonObject ValidExpression(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            return random.Next(2) == 0
                ? new JsonObject { ["i32"] = random.Next(-10, 11) }
                : new JsonObject { ["var"] = Names[random.Next(Names.Length)] };
        }

        return new JsonObject
        {
            ["op"] = random.Next(3) switch { 0 => "add", 1 => "sub", _ => "mul" },
            ["left"] = ValidExpression(random, depth - 1),
            ["right"] = ValidExpression(random, depth - 1)
        };
    }

    private static string ModuleJson(int index, string expression, bool shuffled = false)
        => shuffled ? ShuffledModuleJson(index, expression) : OrderedModuleJson(index, expression);

    private static string OrderedModuleJson(int index, string expression)
        => $$"""
        {
          "id": "fuzz-breadth-{{index}}",
          "version": "1.0.0",
          "functions": [{{FunctionJson(expression, shuffled: false)}}]
        }
        """;

    private static string ShuffledModuleJson(int index, string expression)
        => $$"""
        {
          "functions": [{{FunctionJson(expression, shuffled: true)}}],
          "version": "1.0.0",
          "id": "fuzz-breadth-{{index}}"
        }
        """;

    private static string FunctionJson(string expression, bool shuffled)
        => shuffled ? $$"""
        {
          "body": [{ "op": "return", "value": {{expression}} }],
          "returnType": "I32",
          "parameters": [
            { "type": "I32", "name": "a" },
            { "type": "I32", "name": "b" },
            { "type": "I32", "name": "c" },
            { "type": "I32", "name": "value" }
          ],
          "visibility": "entrypoint",
          "id": "main"
        }
        """ : $$"""
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [
            { "name": "a", "type": "I32" },
            { "name": "b", "type": "I32" },
            { "name": "c", "type": "I32" },
            { "name": "value", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [{ "op": "return", "value": {{expression}} }]
        }
        """;

    private static string Token(Random random)
    {
        var bytes = new byte[4];
        random.NextBytes(bytes);
        return Convert.ToHexString(bytes) +
               random.Next(0, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string PolicyHash(string tenant, long limit, long fuel)
        => SandboxPolicyBuilder.Create()
            .WithPolicyId("fuzz-policy")
            .Grant("fuzz.cap", new Dictionary<string, string>
            {
                ["tenant"] = tenant,
                ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, SandboxEffect.Cpu)
            .WithFuel(fuel)
            .Build()
            .Hash;

    private static ArtifactManifest Manifest(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(1, "fuzz", "module", "plan", "policy", "bindings",
            "runtime", "compiler", "types", "effects", "verifier", "1.0.0", "net10.0", [], hash, DateTimeOffset.UtcNow);
    }
}
