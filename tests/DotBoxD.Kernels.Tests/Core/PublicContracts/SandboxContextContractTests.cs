using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core.PublicContracts;

public sealed class SandboxContextContractTests
{
    [Theory]
    [InlineData("policy")]
    [InlineData("budget")]
    [InlineData("bindings")]
    [InlineData("audit")]
    public void Constructor_rejects_null_required_collaborators(string parameterName)
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => CreateContextWithNull(parameterName));

        Assert.Equal(parameterName, exception.ParamName);
    }

    private static SandboxContext CreateContextWithNull(string parameterName)
    {
        var policy = SandboxPolicyBuilder.Create().Build();
        var budget = new ResourceMeter(policy.ResourceLimits);
        var bindings = new BindingRegistry([]);
        var audit = new InMemoryAuditSink();

        return parameterName switch
        {
            "policy" => new SandboxContext(
                SandboxRunId.New(),
                null!,
                budget,
                bindings,
                audit,
                CancellationToken.None),
            "budget" => new SandboxContext(
                SandboxRunId.New(),
                policy,
                null!,
                bindings,
                audit,
                CancellationToken.None),
            "bindings" => new SandboxContext(
                SandboxRunId.New(),
                policy,
                budget,
                null!,
                audit,
                CancellationToken.None),
            "audit" => new SandboxContext(
                SandboxRunId.New(),
                policy,
                budget,
                bindings,
                null!,
                CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterName), parameterName, null),
        };
    }
}
