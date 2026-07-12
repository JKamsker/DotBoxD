using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Generated;

public sealed class RpcContractManifestTests
{
    [Fact]
    public void Generated_manifest_is_deterministic_and_fingerprinted()
    {
        var first = RpcContractManifest.Create(typeof(IGameService).Assembly);
        var second = RpcContractManifest.Create(typeof(IGameService).Assembly);

        Assert.NotEmpty(first.Services);
        Assert.Equal(first.Serialize(), second.Serialize());
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
        Assert.StartsWith("manifest|1\n", first.Serialize(), StringComparison.Ordinal);
        Assert.Contains(
            first.Services.SelectMany(service => service.Methods),
            method => method.Signature.Contains("property:Health:System.Int32", StringComparison.Ordinal));
    }

    [Fact]
    public void Diff_marks_removed_and_changed_wire_contracts_as_breaking()
    {
        var previous = new RpcContractManifest([
            new RpcContractService("game", "IGame", [
                new RpcContractMethod("get", "old"),
                new RpcContractMethod("remove", "same")])]);
        var current = new RpcContractManifest([
            new RpcContractService("game", "IGame", [
                new RpcContractMethod("get", "new"),
                new RpcContractMethod("add", "new")])]);

        var changes = current.Diff(previous);

        Assert.Contains(changes, change => change.Contract == "game/get" && change.IsBreaking);
        Assert.Contains(changes, change => change.Contract == "game/remove" && change.IsBreaking);
        Assert.Contains(changes, change => change.Contract == "game/add" && !change.IsBreaking);
    }

    [Fact]
    public void Unsupported_manifest_versions_fail_compatibility_assertions()
    {
        var previous = new RpcContractManifest([]) { FormatVersion = 0 };
        var current = new RpcContractManifest([]);

        var change = Assert.Single(current.Diff(previous));

        Assert.Equal(RpcContractChangeKind.UnsupportedVersion, change.Kind);
        Assert.True(change.IsBreaking);
        Assert.Throws<InvalidOperationException>(() => current.EnsureCompatibleWith(previous));
    }

    [Fact]
    public void Manifest_rejects_null_service_graph_entries_at_construction()
    {
        var nullServices = Assert.Throws<ArgumentNullException>(() => new RpcContractManifest(null!));
        Assert.Equal(nameof(RpcContractManifest.Services), nullServices.ParamName);

        var nullService = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractManifest([null!]));
        Assert.Equal(nameof(RpcContractManifest.Services), nullService.ParamName);
    }

    [Fact]
    public void Services_reject_null_contract_fields_at_construction()
    {
        var nullWireName = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractService(null!, "IGame", []));
        Assert.Equal(nameof(RpcContractService.WireName), nullWireName.ParamName);

        var nullContractType = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractService("game", null!, []));
        Assert.Equal(nameof(RpcContractService.ContractType), nullContractType.ParamName);

        var nullMethods = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractService("game", "IGame", null!));
        Assert.Equal(nameof(RpcContractService.Methods), nullMethods.ParamName);

        var nullMethod = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractService("game", "IGame", [null!]));
        Assert.Equal(nameof(RpcContractService.Methods), nullMethod.ParamName);
    }

    [Fact]
    public void Methods_reject_null_contract_fields_at_construction()
    {
        var nullWireName = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractMethod(null!, "signature"));
        Assert.Equal(nameof(RpcContractMethod.WireName), nullWireName.ParamName);

        var nullSignature = Assert.Throws<ArgumentNullException>(() =>
            new RpcContractMethod("get", null!));
        Assert.Equal(nameof(RpcContractMethod.Signature), nullSignature.ParamName);
    }

    [Fact]
    public void Manifest_rejects_duplicate_flattened_wire_contracts_at_construction()
    {
        var duplicateMethods = Assert.Throws<ArgumentException>(() =>
            new RpcContractManifest([
                new RpcContractService("game", "IGame", [
                    new RpcContractMethod("get", "one"),
                    new RpcContractMethod("get", "two")])]));
        Assert.Equal(nameof(RpcContractManifest.Services), duplicateMethods.ParamName);
        Assert.Contains("game/get", duplicateMethods.Message, StringComparison.Ordinal);

        var duplicateServices = Assert.Throws<ArgumentException>(() =>
            new RpcContractManifest([
                new RpcContractService("game", "IGame", [new RpcContractMethod("get", "one")]),
                new RpcContractService("game", "IGameV2", [new RpcContractMethod("get", "two")])]));
        Assert.Equal(nameof(RpcContractManifest.Services), duplicateServices.ParamName);
        Assert.Contains("game/get", duplicateServices.Message, StringComparison.Ordinal);
    }
}
