using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DotBoxD.Services.Generated;

/// <summary>A deterministic, serializable snapshot of source-generated RPC wire contracts.</summary>
public sealed record RpcContractManifest(IReadOnlyList<RpcContractService> Services)
{
    public const int CurrentFormatVersion = 1;

    public IReadOnlyList<RpcContractService> Services { get; init; } = ValidateServices(Services);

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public static RpcContractManifest Create(params Assembly[] assemblies)
        => Create((IEnumerable<Assembly>)assemblies);

    public static RpcContractManifest Create(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }
        var services = GeneratedServiceRegistry.GetServices(assemblies)
            .OrderBy(service => service.ServiceName, StringComparer.Ordinal)
            .ThenBy(service => service.ServiceType.FullName, StringComparer.Ordinal)
            .Select(service => new RpcContractService(
                service.ServiceName,
                TypeName(service.ServiceType),
                service.Methods
                    .OrderBy(method => method.WireName, StringComparer.Ordinal)
                    .ThenBy(method => method.Name, StringComparer.Ordinal)
                    .Select(Method)
                    .ToArray()))
            .ToArray();
        return new RpcContractManifest(services);
    }

    public string Fingerprint
    {
        get
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(Encoding.UTF8.GetBytes(Serialize()))
                .Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    public string Serialize()
    {
        var lines = new List<string> { $"manifest|{FormatVersion}" };
        foreach (var service in Services)
        {
            lines.Add($"service|{Encode(service.WireName)}|{Encode(service.ContractType)}");
            foreach (var method in service.Methods)
            {
                lines.Add($"method|{Encode(method.WireName)}|{Encode(method.Signature)}");
            }
        }

        return string.Join("\n", lines) + "\n";
    }

    public IReadOnlyList<RpcContractChange> Diff(RpcContractManifest previous)
    {
        if (previous is null)
        {
            throw new ArgumentNullException(nameof(previous));
        }

        if (FormatVersion != CurrentFormatVersion || previous.FormatVersion != CurrentFormatVersion)
        {
            return
            [
                new RpcContractChange(
                    RpcContractChangeKind.UnsupportedVersion,
                    "$manifest",
                    previous.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ];
        }

        var current = Flatten(this);
        var old = Flatten(previous);
        var changes = new List<RpcContractChange>();
        foreach (var pair in old)
        {
            if (!current.TryGetValue(pair.Key, out var signature))
            {
                changes.Add(new RpcContractChange(RpcContractChangeKind.Removed, pair.Key, pair.Value, null));
            }
            else if (!string.Equals(pair.Value, signature, StringComparison.Ordinal))
            {
                changes.Add(new RpcContractChange(RpcContractChangeKind.SignatureChanged, pair.Key, pair.Value, signature));
            }
        }

        foreach (var pair in current)
        {
            if (!old.ContainsKey(pair.Key))
            {
                changes.Add(new RpcContractChange(RpcContractChangeKind.Added, pair.Key, null, pair.Value));
            }
        }

        return changes.OrderBy(change => change.Contract, StringComparer.Ordinal).ToArray();
    }

    public void EnsureCompatibleWith(RpcContractManifest previous)
    {
        var breakingChanges = Diff(previous).Where(change => change.IsBreaking).ToArray();
        if (breakingChanges.Length == 0)
        {
            return;
        }

        var contracts = string.Join(", ", breakingChanges.Select(change => change.Contract));
        throw new InvalidOperationException($"RPC contract manifest contains breaking changes: {contracts}.");
    }

    private static RpcContractMethod Method(GeneratedMethod method)
    {
        var parameters = string.Join(",", method.Parameters
            .OrderBy(parameter => parameter.Position)
            .Select(parameter => $"{parameter.Position}:{RpcContractTypeShape.Describe(parameter.Type)}:" +
                $"{parameter.IsCancellationToken}:{parameter.HasDefaultValue}:{Default(parameter.DefaultValue)}"));
        var signature = $"{RpcContractTypeShape.Describe(method.ReturnType)}|" +
            $"{RpcContractTypeShape.Describe(method.ResultType)}|{method.ReturnKind}|" +
            $"{method.ReturnsNestedService}|{parameters}";
        return new RpcContractMethod(method.WireName, signature);
    }

    private static Dictionary<string, string> Flatten(RpcContractManifest manifest)
        => manifest.Services.SelectMany(service => service.Methods.Select(method =>
                new KeyValuePair<string, string>($"{service.WireName}/{method.WireName}", method.Signature)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyList<RpcContractService> ValidateServices(IReadOnlyList<RpcContractService>? services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(Services));
        }

        var uniqueContracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            if (service is null)
            {
                throw new ArgumentNullException(nameof(Services));
            }

            foreach (var method in service.Methods)
            {
                var contract = $"{service.WireName}/{method.WireName}";
                if (!uniqueContracts.Add(contract))
                {
                    throw new ArgumentException(
                        $"RPC contract manifest contains duplicate wire contract '{contract}'.",
                        nameof(Services));
                }
            }
        }

        return services;
    }

    private static string TypeName(Type? type) => type?.FullName ?? string.Empty;

    private static string Default(object? value) => value is null
        ? "null"
        : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}

public sealed record RpcContractService(
    string WireName,
    string ContractType,
    IReadOnlyList<RpcContractMethod> Methods)
{
    public string WireName { get; init; } = WireName ?? throw new ArgumentNullException(nameof(WireName));

    public string ContractType { get; init; } = ContractType ?? throw new ArgumentNullException(nameof(ContractType));

    public IReadOnlyList<RpcContractMethod> Methods { get; init; } = ValidateMethods(Methods);

    private static IReadOnlyList<RpcContractMethod> ValidateMethods(IReadOnlyList<RpcContractMethod>? methods)
    {
        if (methods is null)
        {
            throw new ArgumentNullException(nameof(Methods));
        }

        foreach (var method in methods)
        {
            if (method is null)
            {
                throw new ArgumentNullException(nameof(Methods));
            }
        }

        return methods;
    }
}

public sealed record RpcContractMethod(string WireName, string Signature)
{
    public string WireName { get; init; } = WireName ?? throw new ArgumentNullException(nameof(WireName));

    public string Signature { get; init; } = Signature ?? throw new ArgumentNullException(nameof(Signature));
}

public enum RpcContractChangeKind
{
    Added,
    Removed,
    SignatureChanged,
    UnsupportedVersion
}

public sealed record RpcContractChange(
    RpcContractChangeKind Kind,
    string Contract,
    string? PreviousSignature,
    string? CurrentSignature)
{
    private RpcContractChangeKind _kind = ValidateAndReturn(Kind, Contract, PreviousSignature, CurrentSignature);
    private string _contract = Contract;
    private string? _previousSignature = PreviousSignature;
    private string? _currentSignature = CurrentSignature;

    public RpcContractChangeKind Kind
    {
        get => _kind;
        init
        {
            Validate(value, Contract, PreviousSignature, CurrentSignature);
            _kind = value;
        }
    }

    public string Contract
    {
        get => _contract;
        init
        {
            Validate(Kind, value, PreviousSignature, CurrentSignature);
            _contract = value;
        }
    }

    public string? PreviousSignature
    {
        get => _previousSignature;
        init
        {
            Validate(Kind, Contract, value, CurrentSignature);
            _previousSignature = value;
        }
    }

    public string? CurrentSignature
    {
        get => _currentSignature;
        init
        {
            Validate(Kind, Contract, PreviousSignature, value);
            _currentSignature = value;
        }
    }

    public bool IsBreaking => Kind is RpcContractChangeKind.Removed or RpcContractChangeKind.SignatureChanged or RpcContractChangeKind.UnsupportedVersion;

    private static void Validate(
        RpcContractChangeKind kind,
        string contract,
        string? previousSignature,
        string? currentSignature)
    {
        if (!IsKnownKind(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), kind, "Unknown RPC contract change kind.");
        }

        if (string.IsNullOrWhiteSpace(contract))
        {
            throw new ArgumentException("RPC contract change rows require a contract name.", nameof(Contract));
        }

        switch (kind)
        {
            case RpcContractChangeKind.Added:
                Require(currentSignature, nameof(CurrentSignature));
                break;
            case RpcContractChangeKind.Removed:
                Require(previousSignature, nameof(PreviousSignature));
                break;
            case RpcContractChangeKind.SignatureChanged:
            case RpcContractChangeKind.UnsupportedVersion:
                Require(previousSignature, nameof(PreviousSignature));
                Require(currentSignature, nameof(CurrentSignature));
                break;
        }
    }

    private static void Require(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RPC contract change rows require the signature for this change kind.", paramName);
        }
    }

    private static bool IsKnownKind(RpcContractChangeKind kind)
        => kind is RpcContractChangeKind.Added
            or RpcContractChangeKind.Removed
            or RpcContractChangeKind.SignatureChanged
            or RpcContractChangeKind.UnsupportedVersion;

    private static RpcContractChangeKind ValidateAndReturn(
        RpcContractChangeKind kind,
        string contract,
        string? previousSignature,
        string? currentSignature)
    {
        Validate(kind, contract, previousSignature, currentSignature);
        return kind;
    }
}
