using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyFileGrantMutationTests
{
    [Theory]
    [InlineData("allowCreate", "true", "not supported")]
    [InlineData("allowOverwrite", "false", "not supported")]
    [InlineData("root", "   ", "required")]
    [InlineData("maxBytesPerRun", "-1", "between 0")]
    [InlineData("maxBytesPerRun", "9223372036854775808", "between 0")]
    public async Task File_read_grants_reject_invalid_parameter_shapes(
        string key,
        string value,
        string expectedMessage)
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var parameters = PolicyMutationTestSupport.FileReadParameters(temp.Path);
        parameters[key] = value;

        var ex = await PrepareFileReadThrowsAsync(parameters);

        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-GRANT-PARAM", expectedMessage);
    }

    [Fact]
    public async Task File_read_grants_require_root_and_max_bytes_parameters()
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var missingRoot = PolicyMutationTestSupport.FileReadParameters(temp.Path);
        missingRoot.Remove("root");
        var missingMaxBytes = PolicyMutationTestSupport.FileReadParameters(temp.Path);
        missingMaxBytes.Remove("maxBytesPerRun");

        var rootEx = await PrepareFileReadThrowsAsync(missingRoot);
        var maxBytesEx = await PrepareFileReadThrowsAsync(missingMaxBytes);

        PolicyMutationTestSupport.AssertDiagnostic(
            rootEx,
            "E-POLICY-GRANT-PARAM",
            "parameter 'root' is required");
        PolicyMutationTestSupport.AssertDiagnostic(
            maxBytesEx,
            "E-POLICY-GRANT-PARAM",
            "parameter 'maxBytesPerRun' must be between 0");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775807")]
    public async Task File_read_grants_accept_max_bytes_boundary_values(string maxBytesPerRun)
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var parameters = PolicyMutationTestSupport.FileReadParameters(temp.Path);
        parameters["maxBytesPerRun"] = maxBytesPerRun;
        var policy = new SandboxPolicy(
            "file-read-boundary",
            SandboxEffects.Pure | SandboxEffect.FileRead | SandboxEffect.Concurrency,
            [
                new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()),
                new CapabilityGrant("file.read", parameters)
            ],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));

        var plan = await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
            await PolicyMutationTestSupport.FileReadModuleAsync(),
            policy);

        Assert.Equal("file-read-boundary", plan.Policy.PolicyId);
    }

    [Fact]
    public async Task File_read_relative_root_reports_only_canonical_path_failure()
    {
        var ex = await PrepareFileReadThrowsAsync(
            PolicyMutationTestSupport.FileReadParameters("relative/config"));

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "absolute canonical path");
        Assert.DoesNotContain(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("existing directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_read_whitespace_root_reports_only_required_parameter_failure()
    {
        var parameters = PolicyMutationTestSupport.FileReadParameters("   ");

        var ex = await PrepareFileReadThrowsAsync(parameters);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "parameter 'root' is required");
        Assert.DoesNotContain(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("absolute canonical", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_read_root_rejects_absolute_noncanonical_segments()
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var root = Path.Combine(temp.Path, "..", Path.GetFileName(temp.Path));

        var ex = await PrepareFileReadThrowsAsync(PolicyMutationTestSupport.FileReadParameters(root));

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "absolute canonical path");
    }

    [Fact]
    public async Task File_read_invalid_root_path_reports_canonical_path_failure()
    {
        var ex = await PrepareFileReadThrowsAsync(
            PolicyMutationTestSupport.FileReadParameters("bad\0root"));

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "absolute canonical path");
    }

    [Theory]
    [InlineData("allowCreate")]
    [InlineData("allowOverwrite")]
    public async Task File_write_grants_reject_non_boolean_write_flags(string flag)
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var parameters = PolicyMutationTestSupport.FileWriteParameters(temp.Path);
        parameters[flag] = "maybe";

        var ex = await PrepareFileWriteThrowsAsync(parameters);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            $"parameter '{flag}' must be a boolean");
    }

    private static async Task<SandboxValidationException> PrepareFileReadThrowsAsync(
        Dictionary<string, string> parameters)
    {
        var policy = new SandboxPolicy(
            "file-read-parameters",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [new CapabilityGrant("file.read", parameters)],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));
        return await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.FileReadModuleAsync(),
            policy);
    }

    private static async Task<SandboxValidationException> PrepareFileWriteThrowsAsync(
        Dictionary<string, string> parameters)
    {
        var policy = new SandboxPolicy(
            "file-write-parameters",
            SandboxEffects.Pure | SandboxEffect.FileWrite | SandboxEffect.Audit,
            [new CapabilityGrant("file.write", parameters)],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesWritten: 1024));
        return await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.FileWriteModuleAsync(),
            policy);
    }
}
