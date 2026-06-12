namespace SafeIR.Hosting;

using SafeIR;

public interface ISandboxWorkerClient
{
    ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record SandboxWorkerProfile(
    bool OutOfProcess,
    bool SecretsIsolated,
    bool ResourceLimitsConfigured)
{
    public static SandboxWorkerProfile HardenedOutOfProcess { get; } = new(
        OutOfProcess: true,
        SecretsIsolated: true,
        ResourceLimitsConfigured: true);

    internal bool SatisfiesWorkerProcess
        => OutOfProcess && SecretsIsolated && ResourceLimitsConfigured;

    internal IReadOnlyDictionary<string, string> ToAuditFields()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outOfProcess"] = OutOfProcess.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["secretsIsolated"] = SecretsIsolated.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["resourceLimitsConfigured"] = ResourceLimitsConfigured.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
}

internal sealed record ConfiguredSandboxWorker(
    ISandboxWorkerClient Client,
    SandboxWorkerProfile Profile);
