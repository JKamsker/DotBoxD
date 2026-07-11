using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileSystemArgumentValidationTests
{
    [Fact]
    public async Task ReadTextAsync_rejects_null_context()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                null!,
                new SandboxPath("settings.json"),
                CancellationToken.None));

        Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task WriteTextAsync_rejects_null_context()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                null!,
                new SandboxPath("settings.json"),
                "value",
                CancellationToken.None));

        Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task ReadTextAsync_rejects_null_path()
    {
        using var temp = TempDirectory.Create();
        var context = CreateReadContext(temp.Path);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                context,
                null!,
                CancellationToken.None));

        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public async Task WriteTextAsync_rejects_null_path()
    {
        using var temp = TempDirectory.Create();
        var context = CreateWriteContext(temp.Path);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                context,
                null!,
                "value",
                CancellationToken.None));

        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public async Task WriteTextAsync_rejects_null_text()
    {
        using var temp = TempDirectory.Create();
        var context = CreateWriteContext(temp.Path);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await SafeFileSystem.WriteTextAsync(
                context,
                new SandboxPath("settings.json"),
                null!,
                CancellationToken.None));

        Assert.Equal("text", ex.ParamName);
    }

    private static SandboxContext CreateReadContext(string root)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(root, maxBytesPerRun: 1024)
            .WithFuel(5_000)
            .Build();

        return CreateContext(policy);
    }

    private static SandboxContext CreateWriteContext(string root)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, maxBytesPerRun: 1024, allowCreate: true, allowOverwrite: true)
            .WithFuel(5_000)
            .Build();

        return CreateContext(policy);
    }

    private static SandboxContext CreateContext(SandboxPolicy policy) =>
        new(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
}
