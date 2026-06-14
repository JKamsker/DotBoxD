using DotBoxD.Kernels;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for CMP-0016: the simple filter/formula contracts have no package-backed
/// authoring surface. The fix labels them as host-side contract design guidance only and proves the
/// DotBoxD.Kernels install boundary fails closed if a package claims a filter contract. These tests guard
/// against drift back to presenting filters/formulas as uploadable DotBoxD.Kernels plugin package features.
/// </summary>
public sealed class Fix_CMP_0016_Tests
{
    [Fact]
    public void Filter_and_formula_contracts_are_labeled_host_side_guidance_only()
    {
        var itemContracts = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "samples", "Pushdown", "PluginIpc",
            "DotBoxD.Kernels.PluginIpc.Server.Abstractions",
            "ItemContracts.cs"));
        var formulaContracts = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "samples", "Pushdown", "PluginIpc",
            "DotBoxD.Kernels.PluginIpc.Server.Abstractions",
            "FormulaContracts.cs"));

        foreach (var source in new[] { itemContracts, formulaContracts })
        {
            Assert.Contains("Host-side contract design guidance only", source);
            Assert.Contains("NOT a package-backed DotBoxD.Kernels", source);
            Assert.Contains("IEventKernel", source);
        }
    }

    [Fact]
    public void Simple_contract_example_is_host_side_only_and_avoids_package_backed_surface()
    {
        var example = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "samples", "Kernels",
            "PluginAuthoring",
            "DotBoxD.Kernels.Example.PluginAuthoring",
            "Examples",
            "SimpleContractExamples.cs"));

        // It must self-document as host-side guidance, not a DotBoxD.Kernels plugin package.
        Assert.Contains("host-side contract design guidance only", example);

        // It must not pretend to ship filters/formulas through the DotBoxD.Kernels package boundary.
        Assert.DoesNotContain("InstallAsync", example);
        Assert.DoesNotContain("InstallJsonAsync", example);
        Assert.DoesNotContain("PluginPackageJsonSerializer", example);
        Assert.DoesNotContain("SandboxHost", example);
    }

    [Fact]
    public async Task Install_fails_closed_when_a_package_claims_a_filter_contract()
    {
        // The filter/formula slots are not a package-backed DotBoxD.Kernels surface; the validator must
        // reject any uploaded package whose manifest claims a non-event filter contract.
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Contract = "IItemFilter" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK014" &&
            d.Message.Contains("IEventKernel<TEvent>", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
