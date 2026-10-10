using System.Diagnostics;

namespace DotBoxD.Architecture.Tests;

public sealed class CoverageSourcePathTests
{
    [Theory]
    [InlineData("/workspace/repo/src/CodeGeneration/", "Generator.cs")]
    [InlineData("C:\\workspace\\repo\\src\\CodeGeneration\\", "Generator.cs")]
    [InlineData("/", "workspace/repo/src/CodeGeneration/Generator.cs")]
    public async Task Coverage_reports_merge_the_same_source_line_across_different_roots(
        string sourceRoot,
        string filename)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotboxd-coverage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "eng/scripts"));
            Directory.CreateDirectory(Path.Combine(root, ".config/code-enforcer"));
            Directory.CreateDirectory(Path.Combine(root, "artifacts/coverage"));
            var script = Path.Combine(root, "eng/scripts/check-coverage.ps1");
            File.Copy(Path.Combine(ArchTestSupport.RepositoryRoot(), "eng/scripts/check-coverage.ps1"), script);
            await File.WriteAllTextAsync(Path.Combine(root, ".config/code-enforcer/coverage.json"),
                """
                { "minimumLineCoverage": 100, "minimumBranchCoverage": 100, "areas": [], "criticalAreas": [] }
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/coverage/runtime.cobertura.xml"),
                Report("/workspace/repo/src/", "CodeGeneration/Generator.cs", hits: 0));
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/coverage/generator.cobertura.xml"),
                Report(sourceRoot, filename, hits: 1));

            var startInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start PowerShell.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await standardOutput + await standardError;

            Assert.True(process.ExitCode == 0, output);
            Assert.Contains("line 100% (1/1", output, StringComparison.Ordinal);
            Assert.Contains("branch 100% (2/2", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Report(string sourceRoot, string filename, int hits)
        => $$"""
             <coverage>
               <sources><source>{{sourceRoot}}</source></sources>
               <packages><package name="DotBoxD.Services.SourceGenerator"><classes>
                 <class filename="{{filename}}"><lines>
                   <line number="10" hits="{{hits}}" branch="True" condition-coverage="{{hits * 100}}% ({{hits * 2}}/2)" />
                 </lines></class>
               </classes></package></packages>
             </coverage>
             """;
}
