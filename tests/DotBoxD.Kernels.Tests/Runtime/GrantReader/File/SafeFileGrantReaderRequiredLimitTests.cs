using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Runtime.File;

namespace DotBoxD.Kernels.Tests.Runtime.GrantReader;

public sealed class SafeFileGrantReaderRequiredLimitTests
{
    [Fact]
    public async Task ReadTextAsync_denies_direct_file_read_grant_missing_maxBytesPerRun()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.txt"), "file-ok");
        var context = CreateContext(
            "file-read-missing-max",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                new CapabilityGrant(
                    "file.read",
                    new Dictionary<string, string>
                    {
                        ["root"] = temp.Path
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                context,
                new SandboxPath("settings.txt"),
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
        Assert.Equal(0, context.Budget.FileBytesRead);
    }

    [Fact]
    public async Task WriteTextAsync_denies_direct_file_write_grant_missing_maxBytesPerRun()
    {
        using var temp = TempDirectory.Create();
        var context = CreateContext(
            "file-write-missing-max",
            SandboxEffects.Pure | SandboxEffect.FileWrite,
            [
                new CapabilityGrant(
                    "file.write",
                    new Dictionary<string, string>
                    {
                        ["root"] = temp.Path,
                        ["allowCreate"] = "true",
                        ["allowOverwrite"] = "false"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesWritten: 1024));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                context,
                new SandboxPath("created.txt"),
                "created",
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
        Assert.Equal(0, context.Budget.FileBytesWritten);
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "created.txt")));
    }

    private static SandboxContext CreateContext(
        string policyId,
        SandboxEffect allowedEffects,
        IReadOnlyList<CapabilityGrant> grants,
        ResourceLimits limits)
    {
        var policy = new SandboxPolicy(policyId, allowedEffects, grants, limits);
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
