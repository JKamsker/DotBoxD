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
}
