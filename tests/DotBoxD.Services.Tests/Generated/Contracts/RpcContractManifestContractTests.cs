using Xunit;

namespace DotBoxD.Services.Tests.Generated;

public sealed class RpcContractManifestContractTests
{
    public static TheoryData<Action, string> MalformedContractGraphs() => new()
    {
        { () => _ = new RpcContractManifest(null!), "Services" },
        { () => _ = ValidManifest() with { Services = null! }, "Services" },
        { () => _ = new RpcContractManifest([null!]), "Services" },
        { () => _ = new RpcContractService("game", "IGame", null!), "Methods" },
        { () => _ = ValidService() with { Methods = null! }, "Methods" },
        { () => _ = new RpcContractService("game", "IGame", [null!]), "Methods" },
        { () => _ = new RpcContractService(null!, "IGame", []), "WireName" },
        { () => _ = ValidService() with { WireName = null! }, "WireName" },
        { () => _ = new RpcContractService("game", null!, []), "ContractType" },
        { () => _ = ValidService() with { ContractType = null! }, "ContractType" },
        { () => _ = new RpcContractMethod(null!, "signature"), "WireName" },
        { () => _ = ValidMethod() with { WireName = null! }, "WireName" },
        { () => _ = new RpcContractMethod("get", null!), "Signature" },
        { () => _ = ValidMethod() with { Signature = null! }, "Signature" },
    };

    [Theory]
    [MemberData(nameof(MalformedContractGraphs))]
    public void Public_manifest_records_reject_malformed_contract_graphs(Action create, string paramName)
    {
        var exception = Record.Exception(create);

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }

    [Fact]
    public void Valid_direct_manifest_still_serializes_and_diffs()
    {
        var previous = new RpcContractManifest([
            new RpcContractService("game", "IGame", [
                new RpcContractMethod("get", "old")])]);
        var current = new RpcContractManifest([
            new RpcContractService("game", "IGame", [
                new RpcContractMethod("get", "new"),
                new RpcContractMethod("add", "new")])]);

        var serialized = current.Serialize();
        var changes = current.Diff(previous);

        Assert.StartsWith("manifest|1\n", serialized, StringComparison.Ordinal);
        Assert.Equal(64, current.Fingerprint.Length);
        Assert.Contains(changes, change => change.Contract == "game/get" && change.IsBreaking);
        Assert.Contains(changes, change => change.Contract == "game/add" && !change.IsBreaking);
    }

    [Fact]
    public void Create_rejects_null_assembly_sequence()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => RpcContractManifest.Create((IEnumerable<System.Reflection.Assembly>)null!));

        Assert.Equal("assemblies", exception.ParamName);
    }

    [Fact]
    public void Diff_rejects_null_previous_manifest()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => ValidManifest().Diff(null!));

        Assert.Equal("previous", exception.ParamName);
    }

    [Fact]
    public void EnsureCompatibleWith_allows_identical_manifest()
    {
        var current = ValidManifest();
        var previous = ValidManifest();

        current.EnsureCompatibleWith(previous);
    }

    private static RpcContractManifest ValidManifest()
        => new([ValidService()]);

    private static RpcContractService ValidService()
        => new("game", "IGame", [ValidMethod()]);

    private static RpcContractMethod ValidMethod()
        => new("get", "signature");
}
