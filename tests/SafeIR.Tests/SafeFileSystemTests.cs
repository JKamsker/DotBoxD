using SafeIR;

namespace SafeIR.Tests;

public sealed class SafeFileSystemTests
{
    [Fact]
    public async Task Granted_file_read_is_scoped_and_audited()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "settings.json"), "tenant-settings");

        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config/settings.json"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("tenant-settings", ((StringValue)result.Value!).Value);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "file.readText" && e.Success);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("config/../../secret.txt")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("file:///etc/passwd")]
    public async Task Path_traversal_and_absolute_paths_are_denied(string path)
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson(path));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
