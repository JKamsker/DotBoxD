using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Serialization.Json;

namespace DotBoxD.UI;

public static class UiPackageJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static UiPackage Import(string json, UiPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        try
        {
            if (StrictUtf8.GetByteCount(json) > policy.MaxPackageBytes)
            {
                throw new UiValidationException("UI package exceeds the host byte limit.");
            }

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowDuplicateProperties = false
            });
            UiJsonBudget.Validate(document.RootElement, policy);
            var package = JsonSerializer.Deserialize(json, UiJsonContext.Default.UiPackage)
                ?? throw new UiValidationException("UI package must be an object.");
            UiPackageValidator.Validate(package, policy);
            var normalized = NormalizeKernels(package, policy);
            _ = SerializeBounded(normalized, policy);
            return normalized;
        }
        catch (JsonException ex)
        {
            throw new UiValidationException($"Malformed UI package JSON: {ex.Message}");
        }
        catch (EncoderFallbackException)
        {
            throw new UiValidationException("UI package must contain well-formed UTF-16.");
        }
    }

    public static string Export(UiPackage package, UiPolicy policy)
    {
        UiPackageValidator.Validate(package, policy);
        var normalized = NormalizeKernels(package, policy) with
        {
            Nodes = [.. package.Nodes.OrderBy(n => n.Id).Select(n => n with
            {
                Properties = [.. n.Properties.OrderBy(p => p.Id)]
            })],
            State = [.. package.State.OrderBy(s => s.Id)],
            Events = [.. package.Events.OrderBy(e => e.Id)],
            RemoteEndpoints = [.. package.RemoteEndpoints.Order()]
        };
        return SerializeBounded(normalized, policy);
    }

    private static string SerializeBounded(UiPackage package, UiPolicy policy)
    {
        var json = JsonSerializer.Serialize(package, UiJsonContext.Default.UiPackage);
        if (StrictUtf8.GetByteCount(json) > policy.MaxPackageBytes)
        {
            throw new UiValidationException("UI package exceeds the host byte limit.");
        }

        return json;
    }

    public static string ComputeHash(UiPackage package, UiPolicy policy)
        => Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(Export(package, policy))));

    private static UiPackage NormalizeKernels(UiPackage package, UiPolicy policy)
    {
        var normalized = package with
        {
            Kernels = [.. package.Kernels.OrderBy(k => k.Id).Select(k => k with
            {
                ModuleJson = JsonExporter.Export(JsonImporter.Import(k.ModuleJson))
            })]
        };
        UiPackageValidator.Validate(normalized, policy);
        return normalized;
    }
}
