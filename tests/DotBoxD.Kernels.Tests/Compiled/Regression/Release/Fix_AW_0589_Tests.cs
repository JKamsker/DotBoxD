namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for issue 589: the red-test workflow used a whole-solution
/// post-agent test run as red-proof validation. That step can report a false agent
/// failure after safe outputs already accepted the PR intent, creating noisy aw issues.
/// </summary>
public sealed class Fix_AW_0589_Tests
{
    [Fact]
    public void Red_test_worker_does_not_fail_on_whole_solution_green_post_validation()
    {
        var source = ReadRepositoryText(".github/workflows/library-surprise-red-test.md");
        var lockFile = ReadRepositoryText(".github/workflows/library-surprise-red-test.lock.yml");

        Assert.DoesNotContain("Verify the proposed tests are red", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The proposed red-test PR passed dotnet test", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Verify the proposed tests are red", lockFile, StringComparison.Ordinal);
        Assert.DoesNotContain("The proposed red-test PR passed dotnet test", lockFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Red_test_worker_keeps_focused_red_test_evidence_requirement()
    {
        var source = ReadRepositoryText(".github/workflows/library-surprise-red-test.md");

        Assert.Contains("Failed as expected", source, StringComparison.Ordinal);
        Assert.Contains("dotnet test", source, StringComparison.Ordinal);
        Assert.Contains("replace_with_noop", source, StringComparison.Ordinal);
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
