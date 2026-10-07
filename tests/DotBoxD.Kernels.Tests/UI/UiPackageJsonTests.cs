using System.Text;
using System.Text.Json.Nodes;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiPackageJsonTests
{
    private const string CompactKernel = """
        {"id":"compact","version":"1.0.0","functions":[{"id":"main","visibility":"entrypoint",
        "parameters":[],"returnType":"I32","body":[{"op":"return","value":{"i32":1}}]}]}
        """;

    [Theory]
    [InlineData("Import")]
    [InlineData("Export")]
    [InlineData("Hash")]
    public void Canonical_kernel_expansion_obeys_the_independent_byte_limit(string operation)
    {
        var package = CompactPackage();
        var policy = new UiPolicy { MaxKernelBytes = Encoding.UTF8.GetByteCount(CompactKernel) };
        UiPackageValidator.Validate(package, policy);
        var canonicalJson = UiPackageJson.Export(package, new UiPolicy());
        var wire = JsonNode.Parse(canonicalJson)!;
        Assert.True(Encoding.UTF8.GetByteCount(wire["kernels"]![0]!["moduleJson"]!.GetValue<string>()) > policy.MaxKernelBytes);
        wire["kernels"]![0]!["moduleJson"] = CompactKernel;
        var error = Assert.Throws<UiValidationException>(() => operation switch
        {
            "Import" => UiPackageJson.Import(wire.ToJsonString(), policy).Kernels[0].ModuleJson,
            "Export" => UiPackageJson.Export(package, policy),
            _ => UiPackageJson.ComputeHash(package, policy)
        });
        Assert.Contains("kernel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_kernel_at_the_exact_byte_limit_roundtrips_and_revalidates()
    {
        var package = CompactPackage();
        var canonical = UiPackageJson.Import(UiPackageJson.Export(package, new UiPolicy()), new UiPolicy());
        var policy = new UiPolicy { MaxKernelBytes = Encoding.UTF8.GetByteCount(canonical.Kernels[0].ModuleJson) };
        var json = UiPackageJson.Export(package, policy);
        var imported = UiPackageJson.Import(json, policy);
        UiPackageValidator.Validate(imported, policy);
        Assert.Equal(json, UiPackageJson.Export(imported, policy));
        Assert.Equal(UiPackageJson.ComputeHash(package, policy), UiPackageJson.ComputeHash(imported, policy));
    }

    [Fact]
    public void Canonical_package_expansion_obeys_the_complete_package_byte_limit()
    {
        var json = CompactWireJson();
        var canonical = UiPackageJson.Export(CompactPackage(), new UiPolicy());
        var policy = new UiPolicy { MaxPackageBytes = Encoding.UTF8.GetByteCount(json) };
        Assert.True(Encoding.UTF8.GetByteCount(canonical) > policy.MaxPackageBytes);
        UiPackageValidator.Validate(UiPackageJson.Import(json, new UiPolicy()), policy);
        var error = Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json, policy));
        Assert.Contains("package", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_package_at_the_exact_byte_limit_roundtrips_and_revalidates()
    {
        var canonical = UiPackageJson.Export(CompactPackage(), new UiPolicy());
        var policy = new UiPolicy { MaxPackageBytes = Encoding.UTF8.GetByteCount(canonical) };
        var imported = UiPackageJson.Import(CompactWireJson(), policy);
        UiPackageValidator.Validate(imported, policy);
        Assert.Equal(canonical, UiPackageJson.Export(imported, policy));
        Assert.Equal(UiPackageJson.ComputeHash(CompactPackage(), policy), UiPackageJson.ComputeHash(imported, policy));
    }

    private static string CompactWireJson()
    {
        var wire = JsonNode.Parse(UiPackageJson.Export(CompactPackage(), new UiPolicy()))!;
        wire["kernels"]![0]!["moduleJson"] = CompactKernel;
        return wire.ToJsonString();
    }

    private static UiPackage CompactPackage() => new(1, 1,
        [new UiNode(1, UiPrimitive.Text, [], [])], [], [new UiKernel(1, CompactKernel, "main")], [], []);

    [Fact]
    public void Package_roundtrip_and_canonical_hash_ignore_definition_order_and_kernel_whitespace()
    {
        var policy = new UiPolicy();
        var package = UiTestFixture.Counter();
        var shuffled = package with
        {
            Nodes = [.. package.Nodes.Reverse()],
            State = [.. package.State.Reverse()],
            Events = [.. package.Events.Reverse()],
            Kernels = [.. package.Kernels.Reverse().Select(k => k with { ModuleJson = "\n " + k.ModuleJson + "\n" })]
        };
        var json = UiPackageJson.Export(package, policy);
        Assert.Equal(json, UiPackageJson.Export(shuffled, policy));
        Assert.Equal(UiPackageJson.ComputeHash(package, policy), UiPackageJson.ComputeHash(shuffled, policy));
        Assert.Equal(json, UiPackageJson.Export(UiPackageJson.Import(json, policy), policy));
        Assert.NotEqual(UiPackageJson.ComputeHash(package, policy),
            UiPackageJson.ComputeHash(package with { State = [package.State[0] with { InitialValue = UiValue.FromInt32(1) }, .. package.State.Skip(1)] }, policy));
    }

    [Theory]
    [InlineData("clrType", "Plugin.Button, Plugin")]
    [InlineData("xaml", "<Button Click=\"Escape\" />")]
    [InlineData("markupExtension", "{Binding Host.NativeHandle}")]
    [InlineData("constructor", "System.Diagnostics.Process")]
    public void Unknown_CLR_XAML_and_escape_hatch_properties_are_rejected(string name, string value)
    {
        var json = JsonNode.Parse(UiPackageJson.Export(UiTestFixture.Counter(), new UiPolicy()))!;
        json["nodes"]![1]![name] = value;
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json.ToJsonString(), new UiPolicy()));
    }

    [Fact]
    public void Duplicate_properties_missing_arrays_and_over_limit_bytes_fail_import()
    {
        var json = UiPackageJson.Export(UiTestFixture.Counter(), new UiPolicy());
        var duplicate = json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal);
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(duplicate, new UiPolicy()));
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import("{}", new UiPolicy()));
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json, new UiPolicy { MaxPackageBytes = 10 }));
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json, new UiPolicy { MaxNodes = 1 }));
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import("\ud800", new UiPolicy()));
    }

    [Fact]
    public void Deterministic_adversarial_import_corpus_never_returns_an_unvalidated_package()
    {
        var policy = new UiPolicy();
        var random = new Random(1453);
        var valid = UiPackageJson.Export(UiTestFixture.Counter(), policy);
        for (var iteration = 0; iteration < 400; iteration++)
        {
            var chars = valid.ToCharArray();
            for (var edit = 0; edit < 1 + iteration % 5; edit++)
            {
                chars[random.Next(chars.Length)] = (char)random.Next(32, 127);
            }

            try
            {
                UiPackageValidator.Validate(UiPackageJson.Import(new string(chars), policy), policy);
            }
            catch (UiValidationException) { }
            catch (DotBoxD.Kernels.Model.SandboxValidationException) { }
        }
    }
}
