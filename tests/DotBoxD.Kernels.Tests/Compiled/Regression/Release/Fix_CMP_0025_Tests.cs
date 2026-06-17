namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for CMP-0025: the release-readiness checklist has an executable gate
/// (<c>eng/scripts/check-release-readiness.ps1</c>), but the workflow path did not invoke it.
/// A tag release reuses CI as its verification gate, so CI must run release readiness and the
/// workflow-security gate must keep that wiring from silently disappearing.
/// </summary>
public sealed class Fix_CMP_0025_Tests
{
    [Fact]
    public void Ci_quality_gates_run_release_readiness()
    {
        var ci = ReadRepositoryText(".github/workflows/ci.yml");

        Assert.Contains("check-release-readiness.ps1", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_inherits_ci_release_readiness_gate()
    {
        var release = ReadRepositoryText(".github/workflows/release.yml");
        var ci = ReadRepositoryText(".github/workflows/ci.yml");

        Assert.Contains("uses: ./.github/workflows/ci.yml", release, StringComparison.Ordinal);
        Assert.Contains("check-release-readiness.ps1", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_security_gate_enforces_release_readiness_wiring()
    {
        var script = ReadRepositoryText("eng/scripts/check-release-workflow-security.ps1");

        Assert.Contains("check-release-readiness.ps1", script, StringComparison.Ordinal);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
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
