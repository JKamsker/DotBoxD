using Xunit;

namespace DotBoxD.Services.Tests.Generated;

public sealed class RpcContractManifestSnapshotTests
{
    [Fact]
    public void Manifest_snapshots_nested_method_lists()
    {
        var methods = new List<RpcContractMethod>
        {
            new("call", "v1")
        };
        var manifest = new RpcContractManifest(
        [
            new RpcContractService("game", "IGame", methods)
        ]);
        var expectedSerialization = manifest.Serialize();
        var expectedFingerprint = manifest.Fingerprint;

        methods[0] = new RpcContractMethod("call", "v2");

        var service = Assert.Single(manifest.Services);
        var method = Assert.Single(service.Methods);
        Assert.Equal("v1", method.Signature);
        Assert.Equal(expectedSerialization, manifest.Serialize());
        Assert.Equal(expectedFingerprint, manifest.Fingerprint);
    }
}
