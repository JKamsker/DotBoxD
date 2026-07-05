using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests.Support;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class CanonicalModuleHasherMalformedGraphTests
{
    [Fact]
    public void Serialize_rejects_null_module_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => CanonicalModuleHasher.Serialize(null!));

        Assert.Equal("module", exception.ParamName);
    }

    [Fact]
    public void Hash_rejects_null_module_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => CanonicalModuleHasher.Hash(null!));

        Assert.Equal("module", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(MalformedModules))]
    public void Serialize_rejects_malformed_public_module_graphs(
        SandboxModule module,
        string expectedContractName)
    {
        var exception = Record.Exception(() => CanonicalModuleHasher.Serialize(module));

        AssertMalformedGraphRejected(exception, expectedContractName);
    }

    public static TheoryData<SandboxModule, string> MalformedModules()
        => MalformedModuleGraphTestData.Modules("hasher-null-function", "hasher-malformed");

    private static void AssertMalformedGraphRejected(Exception? exception, string expectedContractName)
        => MalformedModuleGraphTestData.AssertMalformedGraphRejected(exception, expectedContractName);
}
