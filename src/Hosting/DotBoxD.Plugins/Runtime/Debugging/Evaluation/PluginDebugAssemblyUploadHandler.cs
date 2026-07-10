using System.Text.Json;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugAssemblyUploadHandler(PluginDebugSession session)
{
    public PluginDebugHandlerResult Handle(JsonElement payload)
    {
        if (session.Options.EvaluatorProvider.TrustProfile == PluginDebugEvaluationTrustProfile.SandboxOnly)
        {
            return PluginDebugHandlerResult.Error(
                "assemblyUploadDenied",
                "The host-selected sandbox-only evaluator does not accept assemblies.");
        }

        if (!session.IsAttached)
        {
            return PluginDebugHandlerResult.Error("notAttached", "Attach before uploading trusted evaluator assemblies.");
        }

        try
        {
            var fileName = RequiredString(payload, "fileName");
            var content = Convert.FromBase64String(RequiredString(payload, "content"));
            var offset = RequiredInt32(payload, "offset");
            var complete = RequiredBoolean(payload, "complete");
            var received = session.Assemblies.Append(fileName, offset, content, complete);
            return PluginDebugHandlerResult.Ok(new { fileName, received, complete });
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return PluginDebugHandlerResult.Error("assemblyUploadRejected", exception.Message);
        }
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"Assembly upload {name} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static int RequiredInt32(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new ArgumentException($"Assembly upload {name} must be a 32-bit integer.");

    private static bool RequiredBoolean(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ArgumentException($"Assembly upload {name} must be a boolean.");
}
