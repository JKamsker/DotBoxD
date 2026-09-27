using System.Diagnostics;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class Fix_CI_0001_Tests
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(350, true)]
    [InlineData(351, false)]
    public async Task Csharp_file_line_gate_scans_repo_sources_not_only_eng(int lineCount, bool shouldPass)
    {
        var fixture = Directory.CreateTempSubdirectory("dotboxd-line-guard-");
        try
        {
            var scriptDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "eng", "scripts"));
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "src"));
            foreach (var script in new[] { "check-csharp-file-lines.ps1", "code-enforcer-csharp-scan.ps1" })
            {
                File.Copy(Path.Combine(RepositoryRoot(), "eng", "scripts", script), Path.Combine(scriptDirectory.FullName, script));
            }

            var probePath = Path.Combine(sourceDirectory.FullName, "CodeEnforcerOverLimitProbe.cs");
            await File.WriteAllLinesAsync(
                probePath,
                Enumerable.Range(0, lineCount).Select(i => $"// probe {i}"));

            using var process = StartLineGuard(fixture.FullName);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            if (await Task.WhenAny(exitTask, Task.Delay(ScanTimeout)) != exitTask)
            {
                KillProcess(process);
                await exitTask;
                Assert.Fail($"CodeEnforcer did not finish within {ScanTimeout} while scanning a probe file.");
            }

            await exitTask;
            var output = await outputTask;
            var error = await errorTask;

            Assert.True((process.ExitCode == 0) == shouldPass, output + error);
            if (shouldPass)
            {
                Assert.Contains("CodeEnforcer passed.", output, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("CE0001 src/CodeEnforcerOverLimitProbe.cs", output + error, StringComparison.Ordinal);
            }
        }
        finally
        {
            fixture.Delete(recursive: true);
        }
    }

    private static Process StartLineGuard(string fixtureRoot)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = fixtureRoot
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine("eng", "scripts", "check-csharp-file-lines.ps1"));
        startInfo.ArgumentList.Add("-FailAt");
        startInfo.ArgumentList.Add("350");
        RemoveCoverageProfilerEnvironment(startInfo);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
    }

    private static void RemoveCoverageProfilerEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var key in new[]
        {
            "CORECLR_ENABLE_PROFILING",
            "CORECLR_PROFILER",
            "CORECLR_PROFILER_PATH",
            "CORECLR_PROFILER_PATH_32",
            "CORECLR_PROFILER_PATH_64",
            "COR_ENABLE_PROFILING",
            "COR_PROFILER",
            "COR_PROFILER_PATH",
            "COR_PROFILER_PATH_32",
            "COR_PROFILER_PATH_64"
        })
        {
            startInfo.Environment.Remove(key);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
