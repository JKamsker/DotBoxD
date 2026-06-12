namespace SafeIR;

public delegate ValueTask<SandboxValue> BindingInvoker(
    SandboxContext context,
    IReadOnlyList<SandboxValue> args,
    CancellationToken cancellationToken);

public delegate void CapabilityGrantValidator(
    CapabilityGrant grant,
    ICollection<SandboxDiagnostic> diagnostics);

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
    CompiledBinding Compiled);

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
    CapabilityGrantValidator? GrantValidator = null)
{
    public BindingSignature Signature => new(
        Id, Version, Parameters.ToArray(), ReturnType, Effects, RequiredCapability, CostModel, AuditLevel, Safety, Compiled);
}

public interface IBindingCatalog
{
    bool TryGet(string id, out BindingSignature binding);
    bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator);
    IReadOnlyList<BindingSignature> Signatures { get; }
    string ManifestHash { get; }
}

public sealed class BindingRegistry : IBindingCatalog
{
    private readonly Dictionary<string, BindingDescriptor> _bindings;
    private readonly Dictionary<string, CapabilityGrantValidator> _grantValidators;

    public BindingRegistry(IEnumerable<BindingDescriptor> bindings)
    {
        var frozen = bindings.Select(Freeze).ToArray();
        _bindings = frozen.ToDictionary(b => b.Id, StringComparer.Ordinal);
        _grantValidators = frozen
            .Where(b => !string.IsNullOrWhiteSpace(b.RequiredCapability) && b.GrantValidator is not null)
            .GroupBy(b => b.RequiredCapability!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().GrantValidator!, StringComparer.Ordinal);
        ManifestHash = ComputeManifestHash(Signatures);
    }

    public IReadOnlyList<BindingSignature> Signatures => _bindings.Values.Select(b => b.Signature).OrderBy(b => b.Id).ToArray();

    public string ManifestHash { get; }

    public BindingDescriptor GetDescriptor(string id) => _bindings[id];

    public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
    {
        if (_grantValidators.TryGetValue(capabilityId, out var found)) {
            validator = found;
            return true;
        }

        validator = default!;
        return false;
    }

    public bool TryGet(string id, out BindingSignature binding)
    {
        if (_bindings.TryGetValue(id, out var descriptor)) {
            binding = descriptor.Signature;
            return true;
        }

        binding = default!;
        return false;
    }

    private static string ComputeManifestHash(IEnumerable<BindingSignature> signatures)
    {
        var records = new List<string> {
            CanonicalEncoding.Record("bindings-v2")
        };
        records.AddRange(signatures.Select(BindingRecord).Order(StringComparer.Ordinal));
        return CanonicalEncoding.HashRecords(records);
    }

    private static BindingDescriptor Freeze(BindingDescriptor binding)
        => binding with { Parameters = binding.Parameters.ToArray() };

    private static string BindingRecord(BindingSignature binding)
    {
        var fields = new List<string?> {
            "binding",
            binding.Id,
            binding.Version.ToString(),
            Type(binding.ReturnType),
            ((long)binding.Effects).ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.RequiredCapability,
            binding.CostModel.BaseFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.PerByteFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.AllocationFromReturnBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.MaxCallsPerRun?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.AuditLevel.ToString(),
            binding.Safety.ToString(),
            binding.Compiled.Kind,
            binding.Compiled.Type,
            binding.Compiled.Method
        };
        fields.AddRange(binding.Parameters.Select(Type));
        return CanonicalEncoding.Record(fields);
    }

    private static string Type(SandboxType type)
    {
        var fields = new List<string?> { "type", type.Name };
        fields.AddRange(type.Arguments.Select(Type));
        return CanonicalEncoding.Record(fields);
    }
}

public sealed class BindingRegistryBuilder
{
    private readonly List<BindingDescriptor> _bindings = [];

    public BindingRegistryBuilder Add(BindingDescriptor descriptor)
    {
        _bindings.Add(descriptor);
        return this;
    }

    public BindingRegistryBuilder AddRange(IEnumerable<BindingDescriptor> descriptors)
    {
        _bindings.AddRange(descriptors);
        return this;
    }

    public BindingRegistry Build()
    {
        var diagnostics = BindingRegistryValidator.Validate(_bindings);
        if (diagnostics.Count > 0) {
            throw new SandboxValidationException(diagnostics);
        }

        return new BindingRegistry(_bindings);
    }
}

