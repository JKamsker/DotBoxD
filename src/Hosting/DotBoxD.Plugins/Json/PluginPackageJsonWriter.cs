using System.Text.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Json;

using static JsonImport;

/// <summary>
/// Writes the plugin package JSON shape consumed by <see cref="PluginPackageJsonSerializer"/>.
/// The optional <see cref="PluginManifest.RpcEntrypoint"/> is emitted only for server extension kernels.
/// </summary>
internal static class PluginPackageJsonWriter
{
    public static void ValidatePackageForExport(PluginPackage package)
    {
        if (package.Manifest.RpcEntrypoint is not null)
        {
            RpcKernelPackageValidator.Validate(package);
            return;
        }

        PluginPackageValidator.Validate(package);
    }

    public static void WritePackage(Utf8JsonWriter writer, PluginPackage package)
    {
        writer.WriteStartObject();
        WriteManifest(writer, package.Manifest);
        writer.WritePropertyName("entrypoints");
        WriteEntrypoints(writer, package.Entrypoints);
        writer.WritePropertyName("module");
        JsonExporter.Write(writer, package.Module);
        writer.WriteEndObject();
    }

    private static void WriteManifest(Utf8JsonWriter writer, PluginManifest manifest)
    {
        writer.WritePropertyName("manifest");
        writer.WriteStartObject();
        WriteString(writer, "pluginId", manifest.PluginId);
        WriteString(writer, "contract", manifest.Contract);
        WriteString(writer, "mode", manifest.Mode.ToString());
        WriteStringArray(writer, "effects", manifest.Effects);
        WriteLiveSettings(writer, manifest.LiveSettings);
        WriteSubscriptions(writer, manifest.Subscriptions);
        WriteStringArray(writer, "requiredCapabilities", manifest.RequiredCapabilities);
        if (manifest.RpcEntrypoint is { } rpcEntrypoint)
        {
            WriteString(writer, "rpcEntrypoint", rpcEntrypoint);
        }

        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                throw Error("E-JSON-EXPORT", $"{name}[{index}] must be a string, not null");
            }

            WriteStringValue(writer, value, $"{name}[{index}]");
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettings(Utf8JsonWriter writer, IReadOnlyList<LiveSettingDefinition> settings)
    {
        writer.WritePropertyName("liveSettings");
        writer.WriteStartArray();
        foreach (var setting in settings)
        {
            writer.WriteStartObject();
            WriteString(writer, "name", setting.Name);
            WriteString(writer, "type", setting.Type);
            writer.WritePropertyName("defaultValue");
            WriteLiveSettingValue(writer, setting.DefaultValue, "defaultValue");
            if (setting.Min is not null)
            {
                writer.WritePropertyName("min");
                WriteLiveSettingValue(writer, setting.Min, "min");
            }

            if (setting.Max is not null)
            {
                writer.WritePropertyName("max");
                WriteLiveSettingValue(writer, setting.Max, "max");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettingValue(Utf8JsonWriter writer, object? value, string name)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case int integer:
                writer.WriteNumberValue(integer);
                break;
            case long integer:
                writer.WriteNumberValue(integer);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case float number when float.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case string text:
                WriteStringValue(writer, text, name);
                break;
            default:
                throw Error("E-JSON-EXPORT", $"live setting value '{name}' must be a JSON scalar");
        }
    }

    private static void WriteSubscriptions(Utf8JsonWriter writer, IReadOnlyList<HookSubscriptionManifest> subscriptions)
    {
        writer.WritePropertyName("subscriptions");
        writer.WriteStartArray();
        foreach (var subscription in subscriptions)
        {
            writer.WriteStartObject();
            WriteString(writer, "event", subscription.Event);
            WriteString(writer, "kernel", subscription.Kernel);
            WriteIndexedPredicates(writer, subscription.IndexedPredicates);
            if (subscription.IndexCoversPredicate)
            {
                writer.WriteBoolean("indexCoversPredicate", subscription.IndexCoversPredicate);
            }

            // Emitted only for lowered RunLocal chains, so ordinary subscription manifests stay byte-for-byte unchanged.
            if (subscription.LocalTerminal)
            {
                writer.WriteBoolean("localTerminal", subscription.LocalTerminal);
            }

            if (subscription.ProjectedType is { } projectedType)
            {
                WriteString(writer, "projectedType", projectedType);
            }

            if (subscription.Priority != 0)
            {
                writer.WriteNumber("priority", subscription.Priority);
            }

            if (subscription.ResultType is { } resultType)
            {
                WriteString(writer, "resultType", resultType);
            }

            if (subscription.ResultLocalTerminal)
            {
                writer.WriteBoolean("resultLocalTerminal", subscription.ResultLocalTerminal);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // Emitted only when the lowered predicate had index-eligible leaves, so manifests for ordinary
    // chains stay byte-for-byte identical to pre-feature output (and round-trip cleanly).
    private static void WriteIndexedPredicates(Utf8JsonWriter writer, IReadOnlyList<IndexedPredicate> predicates)
    {
        if (predicates.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("indexedPredicates");
        writer.WriteStartArray();
        foreach (var predicate in predicates)
        {
            if (!Enum.IsDefined(predicate.Operator))
            {
                throw new SandboxValidationException(
                    [new SandboxDiagnostic("DBXK046", $"Indexed predicate operator '{predicate.Operator}' is not supported.")]);
            }

            ValidateIndexedPredicateValue(predicate);
            writer.WriteStartObject();
            WriteString(writer, "path", predicate.Path);
            WriteString(writer, "operator", predicate.Operator.ToString());
            writer.WritePropertyName("value");
            WriteLiveSettingValue(writer, predicate.Value, "indexed predicate value");
            WriteString(writer, "valueType", predicate.ValueType);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ValidateIndexedPredicateValue(IndexedPredicate predicate)
    {
        if (predicate.ValueType is not ("bool" or "int" or "long" or "double" or "string"))
        {
            throw Error(
                "DBXK047",
                $"Indexed predicate value type '{predicate.ValueType}' is not supported.");
        }

        if (!IndexedPredicateValueMatchesType(predicate.Value, predicate.ValueType))
        {
            throw Error(
                "DBXK049",
                $"Indexed predicate value '{predicate.Value ?? "null"}' does not match its declared value type '{predicate.ValueType}'.");
        }
    }

    private static bool IndexedPredicateValueMatchesType(object? value, string valueType)
        => valueType switch
        {
            "bool" => value is bool,
            "int" => value is int,
            "long" => value is long,
            "double" => value is double,
            "string" => value is string,
            _ => false
        };

    private static void WriteEntrypoints(Utf8JsonWriter writer, KernelEntrypoints entrypoints)
    {
        writer.WriteStartObject();
        WriteString(writer, "shouldHandle", entrypoints.ShouldHandle);
        WriteString(writer, "handle", entrypoints.Handle);
        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string value)
    {
        RequireWellFormedUtf16(value, name);
        writer.WriteString(name, value);
    }

    private static void WriteStringValue(Utf8JsonWriter writer, string value, string name)
    {
        RequireWellFormedUtf16(value, name);
        writer.WriteStringValue(value);
    }

    private static void RequireWellFormedUtf16(string value, string name)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                throw MalformedUtf16(name);
            }

            if (char.IsLowSurrogate(current))
            {
                throw MalformedUtf16(name);
            }
        }
    }

    private static SandboxValidationException MalformedUtf16(string name)
        => Error("E-JSON-EXPORT", $"'{name}' contains malformed UTF-16 text with an unpaired surrogate");
}
