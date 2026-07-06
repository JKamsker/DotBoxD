using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Core.CacheIdentity;

public sealed class VerificationPolicyHashIdentityTests
{
    [Fact]
    public void Allowlist_hash_distinguishes_delimiter_containing_entries()
    {
        var first = PolicyWithAllowlist(["a", "b|c"]);
        var second = PolicyWithAllowlist(["a|b", "c"]);

        Assert.NotEqual(first.AllowlistHash, second.AllowlistHash);
    }

    [Fact]
    public void Runtime_facade_hash_distinguishes_delimiter_containing_entries()
    {
        var first = PolicyWithRuntimeFacades(["a", "b|c"]);
        var second = PolicyWithRuntimeFacades(["a|b", "c"]);

        Assert.NotEqual(first.RuntimeFacadeHash, second.RuntimeFacadeHash);
    }

    [Fact]
    public async Task Cache_key_changes_when_policy_identity_changes_by_delimiter_containing_entries()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var first = PolicyWithAllowlistAndRuntimeFacades(["a", "b|c"], ["a", "b|c"]);
        var second = PolicyWithAllowlistAndRuntimeFacades(["a|b", "c"], ["a|b", "c"]);

        Assert.NotEqual(
            CacheKeyBuilder.Build(plan, "main", first, optimize: false),
            CacheKeyBuilder.Build(plan, "main", second, optimize: false));
    }

    [Fact]
    public void Allowlist_rejects_null_entries_with_clear_exception()
    {
        var members = new HashSet<string>(StringComparer.Ordinal) { null! };

        var exception = Assert.Throws<ArgumentException>(
            () => VerificationPolicy.BoxedValueDefaults() with { AllowedMembers = members });

        Assert.Equal("AllowedMembers", exception.ParamName);
    }

    private static VerificationPolicy PolicyWithAllowlist(string[] allowedMembers)
        => PolicyWithAllowlistAndRuntimeFacades(allowedMembers, []);

    private static VerificationPolicy PolicyWithRuntimeFacades(string[] runtimeFacadeIdentities)
        => PolicyWithAllowlistAndRuntimeFacades([], runtimeFacadeIdentities);

    private static VerificationPolicy PolicyWithAllowlistAndRuntimeFacades(
        string[] allowedMembers,
        string[] runtimeFacadeIdentities)
        => VerificationPolicy.BoxedValueDefaults() with
        {
            AllowedAssemblies = EmptySet(),
            AllowedAssemblyIdentities = EmptySet(),
            AllowedTypes = EmptySet(),
            AllowedMembers = allowedMembers.ToHashSet(StringComparer.Ordinal),
            ForbiddenTypePrefixes = EmptySet(),
            RuntimeFacadeIdentities = runtimeFacadeIdentities.ToHashSet(StringComparer.Ordinal)
        };

    private static HashSet<string> EmptySet()
        => new(StringComparer.Ordinal);
}
