using System.Text.Json.Nodes;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiPackageJsonTests
{
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
