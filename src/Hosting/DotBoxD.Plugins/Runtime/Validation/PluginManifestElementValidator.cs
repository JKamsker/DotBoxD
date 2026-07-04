using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginManifestElementValidator
{
    public static bool ValidateNoNullElements<T>(
        IReadOnlyList<T> values,
        string collectionName,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not null)
            {
                continue;
            }

            diagnostics.Add(new SandboxDiagnostic(
                "DBXK075",
                $"Plugin manifest {collectionName} cannot contain null entries."));
            return false;
        }

        return true;
    }
}
