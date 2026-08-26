namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for the agentic-workflow failures grouped under issue 1227.
/// </summary>
public sealed class Fix_AW_1227_Tests
{
    private static readonly (string WorkflowName, string Model)[] AgenticWorkflows =
    [
        ("gh-aw-smoke-test", "gpt-5.6-terra"),
        ("library-surprise-dispatcher", "gpt-5.6-terra"),
        ("library-surprise-explore", "gpt-5.6-sol"),
        ("library-surprise-fix", "gpt-5.6-terra"),
        ("library-surprise-red-test", "gpt-5.6-terra"),
        ("library-surprise-sweep", "gpt-5.6-sol"),
    ];

    [Fact]
    public void Agentic_workflows_use_the_model_supported_by_the_pinned_runtime()
    {
        foreach (var (workflowName, model) in AgenticWorkflows)
        {
            var source = ReadRepositoryText($".github/workflows/{workflowName}.md");
            var lockFile = ReadRepositoryText($".github/workflows/{workflowName}.lock.yml");

            Assert.Contains($"model: {model}", source, StringComparison.Ordinal);
            Assert.Contains("version: 0.149.1", source, StringComparison.Ordinal);
            Assert.DoesNotContain("model: gpt-5.5", source, StringComparison.Ordinal);
            Assert.Contains($"\"agent_model\":\"{model}\"", lockFile, StringComparison.Ordinal);
            Assert.Contains($"GH_AW_MODEL_AGENT_CODEX: {model}", lockFile, StringComparison.Ordinal);
            Assert.Contains("\"compiler_version\":\"v0.82.0-jk.2\"", lockFile, StringComparison.Ordinal);
            Assert.Contains("\"engine_versions\":{\"codex\":\"0.149.1\"}", lockFile, StringComparison.Ordinal);
            Assert.Contains("@openai/codex@0.149.1", lockFile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pinned_runtime_maps_each_gpt_5_6_tier_to_supported_providers()
    {
        var lockFile = ReadRepositoryText(".github/workflows/gh-aw-smoke-test.lock.yml");
        foreach (var model in new[] { "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna" })
        {
            var expected = $"\\\"{model}\\\":[\\\"copilot/{model}*\\\",\\\"openai/{model}*\\\"]";
            Assert.Contains(expected, lockFile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Explore_worker_emits_its_ledger_comment_once_with_json_stdin()
    {
        var source = ReadRepositoryText(".github/workflows/library-surprise-explore.md");

        Assert.Contains("Safe-output declarations are immutable", source, StringComparison.Ordinal);
        Assert.Contains("safeoutputs add_comment .", source, StringComparison.Ordinal);
        Assert.Contains("never pass a safe-output", source, StringComparison.Ordinal);
        Assert.Contains("temporary_id` as `comment_id`", source, StringComparison.Ordinal);
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
