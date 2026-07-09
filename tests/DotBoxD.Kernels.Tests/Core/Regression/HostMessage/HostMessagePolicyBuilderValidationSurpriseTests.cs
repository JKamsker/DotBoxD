using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Core.Regression.HostMessage;

public sealed class HostMessagePolicyBuilderValidationSurpriseTests
{
    [Theory]
    [MemberData(nameof(MixedMalformedAllowedTargets))]
    public void GrantHostMessageWrite_rejects_mixed_malformed_allowed_targets(string[] allowedTargets)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHostMessageWrite(allowedTargets: allowedTargets));

        Assert.Equal("allowedTargets", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(MixedMalformedTargetPrefixes))]
    public void GrantHostMessageWrite_rejects_mixed_malformed_target_prefixes(string[] targetPrefixes)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHostMessageWrite(targetPrefixes: targetPrefixes));

        Assert.Equal("targetPrefixes", ex.ParamName);
    }

    public static TheoryData<string[]> MixedMalformedAllowedTargets()
        => new()
        {
            new[] { "player-1", null! },
            new[] { "player-1", "" },
            new[] { "player-1", "   " }
        };

    public static TheoryData<string[]> MixedMalformedTargetPrefixes()
        => new()
        {
            new[] { "team.red.", null! },
            new[] { "team.red.", "" },
            new[] { "team.red.", "\t" }
        };
}
