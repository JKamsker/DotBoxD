namespace SafeIR.Plugins;

using System.Text.Json;
using static SafeIR.JsonImport;

/// <summary>
/// Manifest read/write for <see cref="PluginPackageJsonSerializer"/>. The optional
/// <see cref="PluginManifest.RpcEntrypoint"/> is emitted only for kernel RPC service kernels and read
/// back for them; event kernels omit it (back-compat with manifests exported before it existed).
/// </summary>
public static partial class PluginPackageJsonSerializer
{
    private static void WriteManifest(Utf8JsonWriter writer, PluginManifest manifest)
    {
        writer.WritePropertyName("manifest");
        writer.WriteStartObject();
        writer.WriteString("pluginId", manifest.PluginId);
        writer.WriteString("contract", manifest.Contract);
        writer.WriteString("mode", manifest.Mode.ToString());
        WriteStringArray(writer, "effects", manifest.Effects);
        WriteLiveSettings(writer, manifest.LiveSettings);
        WriteSubscriptions(writer, manifest.Subscriptions);
        WriteStringArray(writer, "requiredCapabilities", manifest.RequiredCapabilities);
        if (manifest.RpcEntrypoint is { } rpcEntrypoint)
        {
            writer.WriteString("rpcEntrypoint", rpcEntrypoint);
        }

        writer.WriteEndObject();
    }

    private static PluginManifest ReadManifest(JsonElement element)
    {
        RequireAllowedProperties(
            element,
            "plugin manifest",
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint"]);

        return new PluginManifest(
            RequiredString(element, "pluginId"),
            RequiredString(element, "contract"),
            ReadExecutionMode(RequiredString(element, "mode")),
            ReadStringArray(RequiredArray(element, "effects"), "effects"),
            ReadLiveSettings(RequiredArray(element, "liveSettings")),
            ReadSubscriptions(RequiredArray(element, "subscriptions")))
        {
            // Optional for back-compat: manifests exported before required-capability derivation omit it.
            RequiredCapabilities = element.TryGetProperty("requiredCapabilities", out var requiredCapabilities)
                ? ReadStringArray(requiredCapabilities, "requiredCapabilities")
                : [],
            // Present only for kernel RPC service kernels; event kernels omit it.
            RpcEntrypoint = element.TryGetProperty("rpcEntrypoint", out var rpcEntrypoint)
                ? ReadStringValue(rpcEntrypoint, "rpcEntrypoint")
                : null
        };
    }
}
