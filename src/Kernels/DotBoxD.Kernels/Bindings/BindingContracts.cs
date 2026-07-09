using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

public delegate ValueTask<SandboxValue> BindingInvoker(
    SandboxContext context,
    IReadOnlyList<SandboxValue> args,
    CancellationToken cancellationToken);

public delegate void CapabilityGrantValidator(
    CapabilityGrant grant,
    ICollection<SandboxDiagnostic> diagnostics);

public delegate bool BindingAuditResourceValidator(
    CapabilityGrant grant,
    SandboxAuditEvent auditEvent);

public enum BindingSafety
{
    PureIntrinsic,
    PureHostFacade,
    ReadOnlyExternal,
    SideEffectingExternal,
    DangerousRequiresReview
}

public sealed record BindingCostModel(
    long BaseFuel,
    long PerByteFuel = 0,
    bool AllocationFromReturnBytes = false,
    int? MaxCallsPerRun = null)
{
    public static BindingCostModel Fixed(long baseFuel) => new(baseFuel);

    public static BindingCostModel PerByte(long baseFuel, long perByteFuel)
        => new(baseFuel, perByteFuel);

    public static BindingCostModel PerReturnedByte(long baseFuel, long perByteFuel)
        => new(baseFuel, perByteFuel, AllocationFromReturnBytes: true);
}

public sealed record CompiledBinding(string Kind, string Type, string Method)
{
    public static CompiledBinding RuntimeStub(string type, string method) => new("RuntimeStub", type, method);
}

public sealed record BindingSignature(
    string Id,
    SemVersion Version,
    IReadOnlyList<SandboxType> Parameters,
    SandboxType ReturnType,
    SandboxEffect Effects,
    string? RequiredCapability,
    BindingCostModel CostModel,
    AuditLevel AuditLevel,
    BindingSafety Safety,
    CompiledBinding Compiled)
{
    private IReadOnlyList<SandboxType> _parameters = BindingParameterTypes.Copy(Parameters, nameof(Parameters));

    public IReadOnlyList<SandboxType> Parameters { get => _parameters; init => _parameters = BindingParameterTypes.Copy(value, nameof(Parameters)); }
    public bool IsAsync { get; init; }
    public string AuditKind { get; init; } = BindingAuditKinds.BindingCall;
}

public sealed record BindingDescriptor(
    string Id,
    SemVersion Version,
    IReadOnlyList<SandboxType> Parameters,
    SandboxType ReturnType,
    SandboxEffect Effects,
    string? RequiredCapability,
    BindingCostModel CostModel,
    AuditLevel AuditLevel,
    BindingSafety Safety,
    BindingInvoker Invoke,
    CompiledBinding Compiled,
    CapabilityGrantValidator? GrantValidator = null,
    BindingAuditResourceValidator? AuditResourceValidator = null)
{
    private IReadOnlyList<SandboxType> _parameters = BindingParameterTypes.Copy(Parameters, nameof(Parameters));

    public IReadOnlyList<SandboxType> Parameters { get => _parameters; init => _parameters = BindingParameterTypes.Copy(value, nameof(Parameters)); }

    public BindingSignature Signature => new(
        Id, Version, CopyParameters(Parameters), ReturnType, Effects, RequiredCapability, CostModel, AuditLevel, Safety, Compiled)
    {
        IsAsync = IsAsync,
        AuditKind = AuditKind
    };

    public bool IsAsync { get; init; }
    public string AuditKind { get; init; } = BindingAuditKinds.BindingCall;

    private static SandboxType[] CopyParameters(IReadOnlyList<SandboxType> parameters)
    {
        if (parameters.Count == 0)
        {
            return Array.Empty<SandboxType>();
        }

        var copy = new SandboxType[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            copy[i] = parameters[i];
        }

        return copy;
    }
}

internal static class BindingParameterTypes
{
    public static IReadOnlyList<SandboxType> Copy(IReadOnlyList<SandboxType>? parameters, string paramName)
    {
        ArgumentNullException.ThrowIfNull(parameters, paramName);
        return ModelCopy.List(parameters);
    }
}

public interface IBindingCatalog
{
    bool TryGet(string id, out BindingSignature binding);
    bool Contains(string id);
    bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator);
    IReadOnlyList<BindingSignature> Signatures { get; }
    string ManifestHash { get; }
}

public sealed class BindingRegistryBuilder
{
    private readonly List<BindingDescriptor> _bindings = [];

    public BindingRegistryBuilder Add(BindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _bindings.Add(descriptor);
        return this;
    }

    public BindingRegistryBuilder AddRange(IEnumerable<BindingDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var validated = new List<BindingDescriptor>();
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null)
            {
                throw new ArgumentException("Binding descriptor sequences cannot contain null descriptors.", nameof(descriptors));
            }

            validated.Add(descriptor);
        }

        _bindings.AddRange(validated);
        return this;
    }

    public BindingRegistry Build()
    {
        var diagnostics = BindingRegistryValidator.Validate(_bindings);
        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }

        return BindingRegistry.FromValidated(_bindings);
    }
}
