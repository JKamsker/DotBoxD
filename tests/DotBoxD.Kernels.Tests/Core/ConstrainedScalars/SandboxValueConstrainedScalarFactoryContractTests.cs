using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxValueConstrainedScalarFactoryContractTests
{
    [Theory]
    [MemberData(nameof(NullFactoryInputs))]
    public void Constrained_scalar_factories_reject_null_inputs_at_public_boundary(
        Action create,
        string paramName)
    {
        var ex = Assert.Throws<ArgumentNullException>(create);

        Assert.Equal(paramName, ex.ParamName);
    }

    public static TheoryData<Action, string> NullFactoryInputs()
        => new()
        {
            { () => SandboxValue.FromOpaqueId(null!, "id"), "typeName" },
            { () => SandboxValue.FromOpaqueId("PlayerId", null!), "value" },
            { () => SandboxValue.FromPath(null!), "value" },
            { () => SandboxValue.FromUri(null!), "value" }
        };

    [Fact]
    public void FromOpaqueId_reports_invalid_type_name_as_typeName()
    {
        var ex = Assert.Throws<ArgumentException>(() => SandboxValue.FromOpaqueId("playerId", "id"));

        Assert.Equal("typeName", ex.ParamName);
    }
}
