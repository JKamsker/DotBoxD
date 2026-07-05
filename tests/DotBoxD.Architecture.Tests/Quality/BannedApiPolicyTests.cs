using System.Diagnostics;

namespace DotBoxD.Architecture.Tests;

public sealed class BannedApiPolicyTests
{
    [Fact]
    public async Task Guard_reports_forbidden_api_with_location_and_remediation()
    {
        using var repo = TempRepo.Create();
        repo.Write(".config/code-enforcer/banned-apis.json", ProcessPolicy("[]"));
        repo.Write(
            "src/Kernels/Forbidden.cs",
            """
            namespace Sample;
            public static class Forbidden
            {
                public static void Run()
                {
                    System.Diagnostics.Process.Start("tool");
                }
            }
            """);

        var result = await RunGuardAsync(repo.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No process spawning", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Kernels/Forbidden.cs:6", result.Output, StringComparison.Ordinal);
        Assert.Contains("Use an approved abstraction.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guard_allows_documented_exceptions()
    {
        using var repo = TempRepo.Create();
        repo.Write(
            ".config/code-enforcer/banned-apis.json",
            ProcessPolicy(
                """
                [
                  {
                    "path": "src/Kernels/Allowed.cs",
                    "reason": "This test fixture owns the approved process abstraction."
                  }
                ]
                """));
        repo.Write(
            "src/Kernels/Allowed.cs",
            """
            namespace Sample;
            public static class Allowed
            {
                public static void Run()
                {
                    System.Diagnostics.Process.Start("tool");
                }
            }
            """);

        var result = await RunGuardAsync(repo.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Banned API policy passed.", result.Output, StringComparison.Ordinal);
    }

    private static string ProcessPolicy(string allowedIn)
        => """
           {
             "rules": [
               {
                 "name": "No process spawning",
                 "forbiddenIn": [ "src/Kernels/**" ],
                 "allowedIn": __ALLOWED_IN__,
                 "symbols": [
                   {
                     "name": "Process.Start",
                     "pattern": "\\b(?:System\\.Diagnostics\\.)?Process\\.Start\\s*\\("
                   }
                 ],
                 "reason": "Process spawning is not allowed in this layer.",
                 "remediation": "Use an approved abstraction."
               }
             ]
           }
           """.Replace("__ALLOWED_IN__", allowedIn, StringComparison.Ordinal);

    private static async Task<CommandResult> RunGuardAsync(string rootPath)
    {
        var scriptPath = Path.Combine(ArchTestSupport.RepositoryRoot(), "eng/scripts/check-banned-apis.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-RootPath");
        startInfo.ArgumentList.Add(rootPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CommandResult(
            process.ExitCode,
            await standardOutput + await standardError);
    }

    private sealed record CommandResult(int ExitCode, string Output);

    private sealed class TempRepo : IDisposable
    {
        private TempRepo(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempRepo Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-banned-api-" + Guid.NewGuid().ToString("N")));

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
