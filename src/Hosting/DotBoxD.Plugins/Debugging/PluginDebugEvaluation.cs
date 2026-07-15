using System.Collections.ObjectModel;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

public enum PluginDebugEvaluationTrustProfile
{
    SandboxOnly,
    TrustedWorker,
    TrustedInProcess
}

/// <summary>Sandbox frame and expression supplied to a host-selected debug evaluator.</summary>
public sealed record PluginDebugEvaluationRequest
{
    public PluginDebugEvaluationRequest(
        ISandboxDebugFrame frame,
        string expression,
        bool allowAwait = false,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? assemblies = null)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        Expression = expression;
        AllowAwait = allowAwait;
        Assemblies = SnapshotAssemblies(assemblies);
    }

    public ISandboxDebugFrame Frame { get; }

    public string Expression { get; }

    public bool AllowAwait { get; }

    /// <summary>Session-scoped assembly images supplied only after trusted capability negotiation.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Assemblies { get; }

    private static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> SnapshotAssemblies(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? assemblies)
    {
        if (assemblies is null || assemblies.Count == 0)
        {
            return new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal));
        }

        var snapshot = new Dictionary<string, ReadOnlyMemory<byte>>(assemblies.Count, StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assembly.Key);
            if (assembly.Value.IsEmpty)
            {
                throw new ArgumentException("Uploaded debug assemblies cannot be empty.", nameof(assemblies));
            }

            snapshot.Add(assembly.Key, assembly.Value.ToArray());
        }

        return new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(snapshot);
    }
}

/// <summary>Sandbox-value-only evaluation result returned across the debug trust boundary.</summary>
public sealed record PluginDebugEvaluationResult
{
    private PluginDebugEvaluationResult(SandboxValue? value, SandboxError? error)
    {
        if ((value is null) == (error is null))
        {
            throw new ArgumentException("An evaluation result must contain exactly one value or error.");
        }

        Value = value;
        Error = error;
    }

    public bool Succeeded => Value is not null;

    public SandboxValue? Value { get; }

    public SandboxError? Error { get; }

    public static PluginDebugEvaluationResult Success(SandboxValue value)
        => new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static PluginDebugEvaluationResult Failure(SandboxError error)
        => new(null, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>Host-selected evaluation provider. Trusted profiles require explicit host configuration.</summary>
public interface IPluginDebugEvaluatorProvider
{
    string Id { get; }

    PluginDebugEvaluationTrustProfile TrustProfile { get; }

    bool SupportsAwait { get; }

    ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
        PluginDebugEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
