using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DotBoxD.Services.Generated;

/// <summary>A deterministic, serializable snapshot of source-generated RPC wire contracts.</summary>
public sealed record RpcContractManifest(IReadOnlyList<RpcContractService> Services)
{
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
        var lines = new List<string>();
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

    private static RpcContractMethod Method(GeneratedMethod method)
    {
        var parameters = string.Join(",", method.Parameters
            .OrderBy(parameter => parameter.Position)
            .Select(parameter => $"{parameter.Position}:{TypeName(parameter.Type)}:" +
                $"{parameter.IsCancellationToken}:{parameter.HasDefaultValue}:{Default(parameter.DefaultValue)}"));
        var signature = $"{TypeName(method.ReturnType)}|{TypeName(method.ResultType)}|{method.ReturnKind}|" +
            $"{method.ReturnsNestedService}|{parameters}";
        return new RpcContractMethod(method.WireName, signature);
    }

    private static Dictionary<string, string> Flatten(RpcContractManifest manifest)
        => manifest.Services.SelectMany(service => service.Methods.Select(method =>
                new KeyValuePair<string, string>($"{service.WireName}/{method.WireName}", method.Signature)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string TypeName(Type? type) => type?.FullName ?? string.Empty;

    private static string Default(object? value) => value is null
        ? "null"
        : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}

public sealed record RpcContractService(
    string WireName,
    string ContractType,
    IReadOnlyList<RpcContractMethod> Methods);

public sealed record RpcContractMethod(string WireName, string Signature);

public enum RpcContractChangeKind
{
    Added,
    Removed,
    SignatureChanged
}

public sealed record RpcContractChange(
    RpcContractChangeKind Kind,
    string Contract,
    string? PreviousSignature,
    string? CurrentSignature)
{
    public bool IsBreaking => Kind is RpcContractChangeKind.Removed or RpcContractChangeKind.SignatureChanged;
}
