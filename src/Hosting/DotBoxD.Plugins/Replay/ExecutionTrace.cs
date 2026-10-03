using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Plugins.Replay;

public sealed record TracePolicy(
    string PolicyId, SandboxEffect AllowedEffects, IReadOnlyList<CapabilityGrant> Grants,
    ResourceLimits ResourceLimits, bool Deterministic, DateTimeOffset? LogicalNow,
    ulong? RandomSeed, IReadOnlyList<string> DeclaredOpaqueIdTypes)
{
    private readonly IReadOnlyList<CapabilityGrant> _grants = Array.AsReadOnly(Grants.ToArray());
    private readonly IReadOnlyList<string> _opaqueTypes = Array.AsReadOnly(DeclaredOpaqueIdTypes.ToArray());
    public IReadOnlyList<CapabilityGrant> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<string> DeclaredOpaqueIdTypes { get => _opaqueTypes; init => _opaqueTypes = Array.AsReadOnly(value.ToArray()); }

    public static TracePolicy Capture(SandboxPolicy policy) => new(policy.PolicyId, policy.AllowedEffects,
        policy.Grants, policy.ResourceLimits, policy.Deterministic, policy.LogicalNow,
        policy.RandomSeed, policy.DeclaredOpaqueIdTypes.Order(StringComparer.Ordinal).ToArray());

    public SandboxPolicy Restore() => new(PolicyId, AllowedEffects, Grants, ResourceLimits,
        Deterministic, LogicalNow, RandomSeed, DeclaredOpaqueIdTypes.ToHashSet(StringComparer.Ordinal));
}

/// <summary>A single boundary call in invocation order. Error text is sensitive and can be transformed before export.</summary>
public sealed record RecordedBindingCall(
    string BindingId, IReadOnlyList<TraceValue> Arguments, TraceValue? Result,
    SandboxError? Error, bool CancellationRequested)
{
    private readonly IReadOnlyList<SandboxAuditEvent> _auditEvents = Array.Empty<SandboxAuditEvent>();
    public IReadOnlyList<SandboxAuditEvent> AuditEvents { get => _auditEvents; init => _auditEvents = Array.AsReadOnly(value.ToArray()); }
    public bool Completed { get; init; } = true;
    private readonly IReadOnlyList<TraceValue> _arguments = Array.AsReadOnly(Arguments.ToArray());
    public IReadOnlyList<TraceValue> Arguments { get => _arguments; init => _arguments = Array.AsReadOnly(value.ToArray()); }
}

public sealed record TraceRuntime(string Framework, string Language, string Compiler, string Verifier, string TypeSystem)
{
    public static TraceRuntime Current => new(Environment.Version.ToString(), SandboxLanguage.CurrentVersionText,
        CacheKeyBuilder.CompilerVersion, VerificationPolicy.BoxedValueDefaults().VerifierVersion, CacheKeyBuilder.TypeSystemVersion);
}

/// <summary>
/// Portable, opt-in recording of one in-process execution. Contains sensitive input/policy/binding data;
/// use <see cref="Redact"/> before export. Redacted traces require explicit replacements before replay.
/// </summary>
public sealed record ExecutionTrace(
    int FormatVersion, TraceRuntime Runtime, string ModuleJson, string ModuleHash, string PolicyHash,
    string BindingManifestHash, TracePolicy Policy, IReadOnlyList<BindingSignature> Bindings,
    string Entrypoint, TraceValue Input, ExecutionMode RequestedMode, ExecutionMode ActualMode,
    bool InitiallyCancelled, IReadOnlyList<RecordedBindingCall> Calls, TraceValue? Result,
    SandboxErrorCode? ErrorCode, bool IsRedacted = false)
{
    private readonly IReadOnlyList<BindingSignature> _bindings = Array.AsReadOnly(Bindings.ToArray());
    private readonly IReadOnlyList<RecordedBindingCall> _calls = Array.AsReadOnly(Calls.ToArray());
    public IReadOnlyList<BindingSignature> Bindings { get => _bindings; init => _bindings = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<RecordedBindingCall> Calls { get => _calls; init => _calls = Array.AsReadOnly(value.ToArray()); }

    public const int CurrentFormatVersion = 1;
    public const int MaximumJsonLength = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 128,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string Serialize()
    {
        var json = JsonSerializer.Serialize(this, Options);
        if (json.Length > MaximumJsonLength)
        {
            throw new InvalidDataException("Execution trace exceeds the size limit.");
        }
        return json;
    }

    public static ExecutionTrace Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumJsonLength)
        {
            throw new InvalidDataException("Execution trace exceeds the size limit.");
        }
        var trace = JsonSerializer.Deserialize<ExecutionTrace>(json, Options)
            ?? throw new InvalidDataException("Expected an execution trace.");
        if (trace.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException("Unsupported execution trace version.");
        }
        return trace;
    }

    /// <summary>
    /// Transforms the entire document (including module literals and policy parameters) before export.
    /// The result is marked redacted so substitutions cannot silently claim exact replay equivalence.
    /// </summary>
    public ExecutionTrace Redact(Func<ExecutionTrace, ExecutionTrace> redact)
    {
        ArgumentNullException.ThrowIfNull(redact);
        return (redact(this) ?? throw new InvalidOperationException("Redaction returned null.")) with { IsRedacted = true };
    }
}

public sealed record ExecutionReplayResult(bool Matches, SandboxExecutionResult Execution, string? Difference);
