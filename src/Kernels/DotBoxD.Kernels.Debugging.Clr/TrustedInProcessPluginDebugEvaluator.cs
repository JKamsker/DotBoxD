using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Debugging;
using Microsoft.CodeAnalysis.Scripting;

namespace DotBoxD.Kernels.Debugging.Clr;

/// <summary>
/// Host configuration for in-process C# evaluation. Values in <see cref="Context"/> are live server objects.
/// </summary>
public sealed record TrustedInProcessDebugEvaluatorOptions
{
    public IReadOnlyCollection<Assembly> References { get; init; } = Array.Empty<Assembly>();

    public IReadOnlyCollection<string> Imports { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, object?> Context { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

/// <summary>Factories that require an explicit trusted-profile choice by the host.</summary>
public static class ClrPluginDebugEvaluators
{
    /// <summary>
    /// Creates full C# evaluation in the server process. This grants arbitrary server-process code execution;
    /// it is not a sandbox and cannot hide server internals from evaluated code.
    /// </summary>
    public static IPluginDebugEvaluatorProvider CreateTrustedInProcess(
        TrustedInProcessDebugEvaluatorOptions options) =>
        new TrustedInProcessPluginDebugEvaluator(options);

    /// <summary>Creates full C# evaluation in a disposable, resource-limited child process.</summary>
    public static IPluginDebugEvaluatorProvider CreateTrustedWorker(TrustedWorkerDebugEvaluatorOptions options) =>
        new TrustedWorkerPluginDebugEvaluator(options);
}

internal sealed class TrustedInProcessPluginDebugEvaluator : IPluginDebugEvaluatorProvider
{
    private readonly IReadOnlyCollection<Assembly> _references;
    private readonly IReadOnlyCollection<string> _imports;
    private readonly IReadOnlyDictionary<string, object?> _context;

    public TrustedInProcessPluginDebugEvaluator(TrustedInProcessDebugEvaluatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.References);
        ArgumentNullException.ThrowIfNull(options.Imports);
        ArgumentNullException.ThrowIfNull(options.Context);
        if (options.References.Any(item => item is null) ||
            options.Imports.Any(string.IsNullOrWhiteSpace) ||
            options.Context.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("CLR evaluator references, imports, and context keys must be valid.", nameof(options));
        }

        _references = options.References.ToArray();
        _imports = options.Imports.ToArray();
        _context = new Dictionary<string, object?>(options.Context, StringComparer.Ordinal);
    }

    public string Id => "trusted-in-process-roslyn-v1";

    public PluginDebugEvaluationTrustProfile TrustProfile => PluginDebugEvaluationTrustProfile.TrustedInProcess;

    public bool SupportsAwait => true;

    public async ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
        PluginDebugEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var value = await ClrDebugEvaluationEngine.EvaluateAsync(
                    request.Expression,
                    request.AllowAwait,
                    ClrDebugEvaluationEngine.Snapshot(request.Frame.Arguments),
                    ClrDebugEvaluationEngine.Snapshot(request.Frame.Locals),
                    _context,
                    _references,
                    _imports,
                    request.Assemblies.Values.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            return PluginDebugEvaluationResult.Success(value.ToSandbox());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CompilationErrorException exception)
        {
            return Failure(string.Join(Environment.NewLine, exception.Diagnostics.Select(item => item.ToString())));
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
    }

    private static PluginDebugEvaluationResult Failure(string message) =>
        PluginDebugEvaluationResult.Failure(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
