using System.Diagnostics;

namespace SafeIR.Tests;

public sealed class SafeFileSystemReparsePointTests
{
    [Fact]
    public async Task File_read_denies_nested_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(outside.Path, "sub"));
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "sub", "secret.txt"), "secret");
        var link = Path.Combine(root.Path, "link");
        if (!TryCreateDirectoryLink(link, outside.Path)) {
            return;
        }

        try {
            var result = await ExecuteReadAsync(root.Path, "link/sub/secret.txt");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        }
        finally {
            TryDeleteDirectoryLink(link);
        }
    }

    [Fact]
    public async Task File_read_denies_terminal_file_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var secret = Path.Combine(outside.Path, "secret.txt");
        await File.WriteAllTextAsync(secret, "secret");
        var link = Path.Combine(root.Path, "secret.txt");
        if (!TryCreateFileLink(link, secret)) {
            return;
        }

        try {
            var result = await ExecuteReadAsync(root.Path, "secret.txt");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        }
        finally {
            TryDeleteFileLink(link);
        }
    }

    private static async Task<SandboxExecutionResult> ExecuteReadAsync(string root, string path)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson(path));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(root, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException) {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (UnauthorizedAccessException) {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (PlatformNotSupportedException) {
            return TryCreateDirectoryJunction(link, target);
        }
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException) {
            return false;
        }
        catch (UnauthorizedAccessException) {
            return false;
        }
        catch (PlatformNotSupportedException) {
            return false;
        }
    }

    private static bool TryCreateDirectoryJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) {
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        process?.WaitForExit();
        return process?.ExitCode == 0 && Directory.Exists(link);
    }

    private static void TryDeleteDirectoryLink(string link)
    {
        try {
            if (Directory.Exists(link)) {
                Directory.Delete(link);
            }
        }
        catch (IOException) {
        }
        catch (UnauthorizedAccessException) {
        }
    }

    private static void TryDeleteFileLink(string link)
    {
        try {
            if (File.Exists(link)) {
                File.Delete(link);
            }
        }
        catch (IOException) {
        }
        catch (UnauthorizedAccessException) {
        }
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
