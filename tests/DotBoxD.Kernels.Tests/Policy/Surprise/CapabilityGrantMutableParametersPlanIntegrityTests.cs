using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class CapabilityGrantMutableParametersPlanIntegrityTests
{
    [Fact]
    public void CapabilityGrant_parameters_initializer_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CapabilityGrant("file.read", new Dictionary<string, string>())
            {
                Parameters = null!
            });
    }

    [Fact]
    public void CapabilityGrant_with_expression_snapshots_parameters()
    {
        var original = new CapabilityGrant("file.read", new Dictionary<string, string>
        {
            ["root"] = "original"
        });
        var replacement = new Dictionary<string, string>
        {
            ["root"] = "prepared"
        };

        var updated = original with { Parameters = replacement };

        replacement["root"] = "mutated";

        Assert.Equal("original", original.Parameters["root"]);
        Assert.Equal("prepared", updated.Parameters["root"]);
    }

    [Fact]
    public async Task Prepared_file_read_plan_does_not_observe_mutated_grant_parameters()
    {
        using var preparedRoot = PolicyMutationTestSupport.TempDirectory.Create();
        using var mutatedRoot = PolicyMutationTestSupport.TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(preparedRoot.Path, "settings.json"), "prepared");
        await File.WriteAllTextAsync(Path.Combine(mutatedRoot.Path, "settings.json"), "mutated");

        var parameters = PolicyMutationTestSupport.FileReadParameters(preparedRoot.Path);
        var grant = new CapabilityGrant("file.read", new Dictionary<string, string>())
        {
            Parameters = parameters
        };
        var policy = new SandboxPolicy(
            "mutable-grant-parameters",
            SandboxEffects.Pure | SandboxEffect.FileRead | SandboxEffect.Concurrency,
            [
                new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()),
                grant
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesRead: 1024));
        var host = PolicyMutationTestSupport.CreateDefaultHost();
        var module = await PolicyMutationTestSupport.FileReadModuleAsync();
        var plan = await host.PrepareAsync(module, policy);
        var preparedPolicyHash = policy.Hash;

        parameters["root"] = mutatedRoot.Path;

        Assert.Equal(preparedPolicyHash, policy.Hash);
        Assert.Equal(preparedPolicyHash, plan.PolicyHash);

        SandboxExecutionResult result;
        try
        {
            result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
        }
        catch (SandboxValidationException ex)
        {
            Assert.Contains(ex.Diagnostics, d => d.Code == "E-PLAN-INTEGRITY");
            return;
        }

        if (!result.Succeeded)
        {
            Assert.True(
                result.Error!.Code is SandboxErrorCode.PermissionDenied or SandboxErrorCode.PolicyDenied,
                result.Error.SafeMessage);
            return;
        }

        var value = Assert.IsType<StringValue>(result.Value);
        Assert.Equal("prepared", value.Value);
    }
}
