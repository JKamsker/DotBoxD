using System.Diagnostics;

namespace DotBoxD.Kernels.Tests.Tooling;

public sealed class ApiBaselineScriptTests
{
    [Fact]
    public async Task Normalize_api_line_preserves_double_slash_inside_string_literals()
    {
        var scriptPath = Path.Combine(
            RepositoryRoot(),
            "eng",
            "scripts",
            "check-api-compat-baseline.ps1");
        var probe = Path.Combine(Path.GetTempPath(), "dotboxd-api-baseline-probe-" + Guid.NewGuid() + ".ps1");
        await File.WriteAllTextAsync(
            probe,
            $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(scriptPath)}}'
            $result = Normalize-ApiLine 'public const string Endpoint = "https://example.test/api";'
            [Console]::Out.Write($result)
            """);

        try
        {
            var output = await RunPowerShellAsync(probe);

            Assert.Equal("public const string Endpoint = \"https://example.test/api\"", output);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static async Task<string> RunPowerShellAsync(string scriptPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "pwsh",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-File", scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start pwsh.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string EscapePowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

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
