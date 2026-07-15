namespace DotBoxD.Plugins.Debugging;

/// <summary>Host-controlled limits and pause policy for remote kernel debugging.</summary>
public sealed record PluginRemoteDebugOptions
{
    private static readonly KernelDebugPauseScope[] ServerOnly = [KernelDebugPauseScope.Server];

    /// <summary>Whether this server accepts remote debugger sessions. Defaults to <see langword="false"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>The pause scope used when the client does not request a narrower allowed scope.</summary>
    public KernelDebugPauseScope DefaultPauseScope { get; init; } = KernelDebugPauseScope.Server;

    /// <summary>Scopes the host permits a debugger to request.</summary>
    public IReadOnlyCollection<KernelDebugPauseScope> AllowedPauseScopes { get; init; } = ServerOnly;

    /// <summary>Maximum time an execution may remain stopped without a debugger renewal.</summary>
    public TimeSpan StopLease { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum serialized size of a frame or variable snapshot.</summary>
    public int MaxSnapshotBytes { get; init; } = 1024 * 1024;

    /// <summary>Maximum condition, watch, logpoint, or console expression length.</summary>
    public int MaxExpressionLength { get; init; } = 4096;

    /// <summary>Maximum trusted-evaluator assembly upload size.</summary>
    public int MaxAssemblyUploadBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum encoded request, response, or event envelope size.</summary>
    public int MaxMessageBytes { get; init; } = 1024 * 1024;

    /// <summary>Host-selected expression evaluator. Defaults to the sandbox-only provider.</summary>
    public IPluginDebugEvaluatorProvider EvaluatorProvider { get; init; } = SandboxOnlyPluginDebugEvaluator.Instance;

    /// <summary>Validates host configuration and throws for unsafe or contradictory limits.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(DefaultPauseScope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultPauseScope),
                DefaultPauseScope,
                "The default debug pause scope is not supported.");
        }

        ArgumentNullException.ThrowIfNull(AllowedPauseScopes);
        if (AllowedPauseScopes.Count == 0)
        {
            throw new ArgumentException("At least one debug pause scope must be allowed.", nameof(AllowedPauseScopes));
        }

        var allowed = new HashSet<KernelDebugPauseScope>();
        foreach (var scope in AllowedPauseScopes)
        {
            if (!Enum.IsDefined(scope))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(AllowedPauseScopes),
                    scope,
                    "An allowed debug pause scope is not supported.");
            }

            allowed.Add(scope);
        }

        if (!allowed.Contains(DefaultPauseScope))
        {
            throw new ArgumentException(
                "The default debug pause scope must also be present in the allowed scopes.",
                nameof(AllowedPauseScopes));
        }

        if (StopLease <= TimeSpan.Zero || StopLease.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StopLease),
                StopLease,
                "The stop lease must be positive and within the platform timer range.");
        }

        EnsurePositive(MaxSnapshotBytes, nameof(MaxSnapshotBytes));
        EnsurePositive(MaxExpressionLength, nameof(MaxExpressionLength));
        EnsurePositive(MaxAssemblyUploadBytes, nameof(MaxAssemblyUploadBytes));
        EnsurePositive(MaxMessageBytes, nameof(MaxMessageBytes));
        ArgumentNullException.ThrowIfNull(EvaluatorProvider);
    }

    internal IReadOnlySet<KernelDebugPauseScope> SnapshotAllowedPauseScopes()
    {
        Validate();
        return AllowedPauseScopes.ToHashSet();
    }

    private static void EnsurePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The debug limit must be positive.");
        }
    }
}
