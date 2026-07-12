using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

public sealed class BindingRegistry : IBindingCatalog
{
    private readonly Dictionary<string, BindingDescriptor> _bindings;
    private readonly Dictionary<string, BindingSignature> _signaturesById;
    private readonly IReadOnlyList<BindingSignature> _signatures;
    private readonly Dictionary<string, CapabilityGrantValidator> _grantValidators;

    public BindingRegistry(IEnumerable<BindingDescriptor> bindings)
        : this(bindings, validate: true)
    {
    }

    private BindingRegistry(IEnumerable<BindingDescriptor> bindings, bool validate)
    {
        var frozen = FreezeAll(bindings);
        if (validate)
        {
            var diagnostics = BindingRegistryValidator.Validate(frozen);
            if (diagnostics.Count > 0)
            {
                throw new SandboxValidationException(diagnostics);
            }
        }

        _bindings = CreateBindingDictionary(frozen);
        _signatures = CreateSignatures(frozen);
        _signaturesById = CreateSignatureDictionary(_signatures);
        _grantValidators = CreateGrantValidators(frozen);
        ManifestHash = ComputeManifestHash(_signatures);
    }

    public IReadOnlyList<BindingSignature> Signatures => _signatures;

    public string ManifestHash { get; }

    internal static BindingRegistry FromValidated(IReadOnlyList<BindingDescriptor> bindings)
        => new(bindings, validate: false);

    public BindingDescriptor GetDescriptor(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _bindings[id];
    }

    public bool Contains(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _bindings.ContainsKey(id);
    }

    internal bool TryGetDescriptor(string id, out BindingDescriptor descriptor)
        => _bindings.TryGetValue(id, out descriptor!);

    public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        if (_grantValidators.TryGetValue(capabilityId, out var found))
        {
            validator = found;
            return true;
        }

        validator = default!;
        return false;
    }

    public bool TryGet(string id, out BindingSignature binding)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_signaturesById.TryGetValue(id, out var signature))
        {
            binding = signature;
            return true;
        }

        binding = default!;
        return false;
    }

    private static BindingDescriptor[] FreezeAll(IEnumerable<BindingDescriptor> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings is IReadOnlyCollection<BindingDescriptor> collection)
        {
            var frozen = new BindingDescriptor[collection.Count];
            var index = 0;
            foreach (var binding in bindings)
            {
                EnsureDescriptor(binding, nameof(bindings));
                frozen[index++] = Freeze(binding);
            }

            return frozen;
        }

        var list = new List<BindingDescriptor>();
        foreach (var binding in bindings)
        {
            EnsureDescriptor(binding, nameof(bindings));
            list.Add(Freeze(binding));
        }

        return list.ToArray();
    }

    private static void EnsureDescriptor(BindingDescriptor? binding, string parameterName)
    {
        if (binding is null)
        {
            throw new ArgumentException("Binding registry bindings cannot contain null descriptors.", parameterName);
        }
    }

    private static Dictionary<string, BindingDescriptor> CreateBindingDictionary(IReadOnlyList<BindingDescriptor> bindings)
    {
        var dictionary = new Dictionary<string, BindingDescriptor>(bindings.Count, StringComparer.Ordinal);
        for (var i = 0; i < bindings.Count; i++)
        {
            dictionary.Add(bindings[i].Id, bindings[i]);
        }

        return dictionary;
    }

    private static IReadOnlyList<BindingSignature> CreateSignatures(IReadOnlyList<BindingDescriptor> bindings)
    {
        var signatures = new BindingSignature[bindings.Count];
        for (var i = 0; i < bindings.Count; i++)
        {
            signatures[i] = bindings[i].Signature;
        }

        Array.Sort(signatures, static (left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
        return Array.AsReadOnly(signatures);
    }

    private static Dictionary<string, BindingSignature> CreateSignatureDictionary(IReadOnlyList<BindingSignature> signatures)
    {
        var dictionary = new Dictionary<string, BindingSignature>(signatures.Count, StringComparer.Ordinal);
        for (var i = 0; i < signatures.Count; i++)
        {
            dictionary.Add(signatures[i].Id, signatures[i]);
        }

        return dictionary;
    }

    private static Dictionary<string, CapabilityGrantValidator> CreateGrantValidators(IReadOnlyList<BindingDescriptor> bindings)
    {
        var grouped = new Dictionary<string, List<CapabilityGrantValidator>>(StringComparer.Ordinal);
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (string.IsNullOrWhiteSpace(binding.RequiredCapability) || binding.GrantValidator is null)
            {
                continue;
            }

            if (!grouped.TryGetValue(binding.RequiredCapability, out var validators))
            {
                validators = [];
                grouped.Add(binding.RequiredCapability, validators);
            }

            validators.Add(binding.GrantValidator);
        }

        var result = new Dictionary<string, CapabilityGrantValidator>(grouped.Count, StringComparer.Ordinal);
        foreach (var item in grouped)
        {
            var validators = item.Value;
            result.Add(
                item.Key,
                validators.Count == 1 ? validators[0] : ComposeGrantValidators(validators));
        }

        return result;
    }

    private static string ComputeManifestHash(IReadOnlyList<BindingSignature> signatures)
    {
        var records = new List<string>(signatures.Count + 1) {
            CanonicalEncoding.Record("bindings-v2")
        };
        for (var i = 0; i < signatures.Count; i++)
        {
            records.Add(BindingRecord(signatures[i]));
        }

        if (records.Count > 2)
        {
            records.Sort(1, records.Count - 1, StringComparer.Ordinal);
        }

        return CanonicalEncoding.HashRecords(records);
    }

    private static BindingDescriptor Freeze(BindingDescriptor binding)
        => binding with { Parameters = CopyParameters(binding.Parameters) };

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

    private static CapabilityGrantValidator ComposeGrantValidators(IReadOnlyList<CapabilityGrantValidator> validators)
        => (grant, diagnostics) =>
        {
            foreach (var validator in validators)
            {
                validator(grant, diagnostics);
            }
        };

    private static string BindingRecord(BindingSignature binding)
    {
        var fields = new List<string?>(15 + binding.Parameters.Count) {
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
            binding.AuditKind,
            binding.Safety.ToString(),
            binding.IsAsync.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.Compiled.Kind,
            binding.Compiled.Type,
            binding.Compiled.Method
        };
        for (var i = 0; i < binding.Parameters.Count; i++)
        {
            fields.Add(Type(binding.Parameters[i]));
        }

        return CanonicalEncoding.Record(fields);
    }

    private static string Type(SandboxType type)
    {
        var fields = new List<string?>(2 + type.Arguments.Count) { "type", type.Name };
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            fields.Add(Type(type.Arguments[i]));
        }

        return CanonicalEncoding.Record(fields);
    }
}
