namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for the agentic-workflow failures grouped under issue 1227.
/// </summary>
public sealed class Fix_AW_1227_Tests
{
    private static readonly string[] AgenticWorkflowNames =
    [
        "gh-aw-smoke-test",
        "library-surprise-dispatcher",
        "library-surprise-explore",
        "library-surprise-fix",
        "library-surprise-red-test",
        "library-surprise-sweep",
    ];

    [Fact]
    public void Agentic_workflows_use_the_model_supported_by_the_pinned_runtime()
    {
        foreach (var workflowName in AgenticWorkflowNames)
        {
            var source = ReadRepositoryText($".github/workflows/{workflowName}.md");
            var lockFile = ReadRepositoryText($".github/workflows/{workflowName}.lock.yml");

            Assert.Contains("model: gpt-5.5", source, StringComparison.Ordinal);
            Assert.DoesNotContain("model: gpt-5.6", source, StringComparison.Ordinal);
            Assert.Contains("\"agent_model\":\"gpt-5.5\"", lockFile, StringComparison.Ordinal);
            Assert.DoesNotContain("GH_AW_MODEL_AGENT_CODEX: gpt-5.6", lockFile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Editing_workers_install_and_probe_apply_patch_before_the_agent_runs()
    {
        foreach (var workflowName in new[] { "library-surprise-red-test", "library-surprise-fix" })
        {
            var source = ReadRepositoryText($".github/workflows/{workflowName}.md");
            var lockFile = ReadRepositoryText($".github/workflows/{workflowName}.lock.yml");

            Assert.Contains("Install Codex apply_patch compatibility shim", source, StringComparison.Ordinal);
            Assert.Contains("Install Codex apply_patch compatibility shim", lockFile, StringComparison.Ordinal);
            Assert.Contains("bash eng/scripts/install-codex-apply-patch.sh", source, StringComparison.Ordinal);
        }

        var shim = ReadRepositoryText("eng/scripts/install-codex-apply-patch.sh");
        Assert.Contains("ln -sfn \"$native_codex\" \"$shim\"", shim, StringComparison.Ordinal);
        Assert.Contains("grep -Fxq 'ready'", shim, StringComparison.Ordinal);
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
