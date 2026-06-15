using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for CMP-0016: the simple filter/formula contracts have no package-backed
/// authoring surface. The fix labels them as host-side contract design guidance only and proves the
/// DotBoxD.Kernels install boundary fails closed if a package claims a filter contract. These tests guard
/// against drift back to presenting filters/formulas as uploadable DotBoxD.Kernels plugin package features.
/// </summary>
public sealed class Fix_CMP_0016_Tests
{
    [Fact]
    public void Filter_and_formula_fixture_contracts_are_labeled_host_side_guidance_only()
    {
        var itemContracts = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tests",
            "DotBoxD.Kernels.TestFixtures.PluginAbstractions",
            "ItemContracts.cs"));
        var formulaContracts = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tests",
            "DotBoxD.Kernels.TestFixtures.PluginAbstractions",
            "FormulaContracts.cs"));

        foreach (var source in new[] { itemContracts, formulaContracts })
        {
            Assert.Contains("Host-side contract design guidance only", source);
            Assert.Contains("NOT a package-backed DotBoxD.Kernels", source);
            Assert.Contains("IEventKernel", source);
        }
    }

    [Fact]
    public async Task Install_fails_closed_when_a_package_claims_a_filter_contract()
    {
        // The filter/formula slots are not a package-backed DotBoxD.Kernels surface; the validator must
        // reject any uploaded package whose manifest claims a non-event filter contract.
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
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
