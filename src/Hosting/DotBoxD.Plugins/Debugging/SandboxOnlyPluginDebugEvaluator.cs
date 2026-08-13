namespace DotBoxD.Plugins.Debugging;

/// <summary>Default side-effect-free evaluator over sandbox arguments and locals only.</summary>
public sealed class SandboxOnlyPluginDebugEvaluator : IPluginDebugEvaluatorProvider
{
    private SandboxOnlyPluginDebugEvaluator()
    {
    }

    public static SandboxOnlyPluginDebugEvaluator Instance { get; } = new();

    public string Id => "sandbox-only-v1";

    public PluginDebugEvaluationTrustProfile TrustProfile => PluginDebugEvaluationTrustProfile.SandboxOnly;

    public bool SupportsAwait => false;

    public ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
        PluginDebugEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SandboxDebugExpressionEvaluator.Evaluate(request));
    }
}
