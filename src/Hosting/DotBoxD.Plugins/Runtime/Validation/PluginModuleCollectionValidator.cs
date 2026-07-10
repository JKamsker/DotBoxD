using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginModuleCollectionValidator
{
    public static bool Validate(SandboxModule module, List<SandboxDiagnostic> diagnostics)
    {
        var hasErrors = false;
        hasErrors |= ValidateCapabilityRequests(module.CapabilityRequests, diagnostics);
        hasErrors |= ValidateFunctions(module.Functions, diagnostics);
        return hasErrors;
    }

    private static bool ValidateCapabilityRequests(
        IReadOnlyList<CapabilityRequest>? capabilityRequests,
        List<SandboxDiagnostic> diagnostics)
    {
        if (capabilityRequests is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK045",
                "Plugin module capabilityRequests collection must not be null."));
            return true;
        }

        var hasErrors = ValidateNoNullElements(
            capabilityRequests,
            "capabilityRequests",
            diagnostics);
        for (var i = 0; i < capabilityRequests.Count; i++)
        {
            var request = capabilityRequests[i];
            if (request is null)
            {
                continue;
            }

            hasErrors |= ValidateCapabilityRequestId(request.Id, i, diagnostics);
        }

        return hasErrors;
    }

    private static bool ValidateFunctions(
        IReadOnlyList<SandboxFunction>? functions,
        List<SandboxDiagnostic> diagnostics)
    {
        if (functions is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK045",
                "Plugin module functions collection must not be null."));
            return true;
        }

        return ValidateNoNullElements(
            functions,
            "functions",
            diagnostics);
    }

    private static bool ValidateNoNullElements<T>(
        IReadOnlyList<T> values,
        string collectionName,
        List<SandboxDiagnostic> diagnostics)
    {
        var hasErrors = false;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not null)
            {
                continue;
            }

            hasErrors = true;
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK045",
                $"Plugin module {collectionName} entry at index {i} must not be null."));
        }

        return hasErrors;
    }

    private static bool ValidateCapabilityRequestId(
        string? capabilityId,
        int index,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(capabilityId) && !capabilityId.Any(char.IsControl))
        {
            return false;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "E-IR-ID",
            "Plugin module capabilityRequests entry at index " +
            $"{index} capability id must be non-empty and must not contain control characters."));
        return true;
    }
}
