using System.Collections.Immutable;
using System.Text.Json;

namespace DotBoxD.UI.Runtime;

/// <summary>Host-selected, policy-gated schemas and implementations; no CLR name resolution.</summary>
public sealed class UiExtensionRegistry<T> where T : IUiExtensionSchema
{
    private readonly ImmutableDictionary<string, T> _schemas;
    public UiExtensionRegistry(IEnumerable<T> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var registered = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            if (schema is null)
            {
                throw new ArgumentException("Schema entries cannot be null.", nameof(schemas));
            }

            registered.Add(schema.SchemaId, schema);
        }

        _schemas = registered.ToImmutable();
    }

    public ImmutableHashSet<string> Schemas => _schemas.Keys.ToImmutableHashSet(StringComparer.Ordinal);

    public ImmutableDictionary<int, UiResolvedExtension<T>> Resolve(UiPackage package, UiPolicy policy)
    {
        UiPackageValidator.Validate(package, policy);
        var resolved = ImmutableDictionary.CreateBuilder<int, UiResolvedExtension<T>>();
        foreach (var extension in package.Extensions)
        {
            if (!_schemas.TryGetValue(extension.SchemaId, out var schema))
            { throw new UiValidationException("UI extension has no trusted renderer registration."); }
            using var payload = JsonDocument.Parse(extension.PayloadJson);
            schema.Validate(payload.RootElement);
            resolved.Add(extension.NodeId, new UiResolvedExtension<T>(schema, payload.RootElement.Clone()));
        }
        return resolved.ToImmutable();
    }
}

public sealed record UiResolvedExtension<T>(T Schema, JsonElement Payload) where T : IUiExtensionSchema;
