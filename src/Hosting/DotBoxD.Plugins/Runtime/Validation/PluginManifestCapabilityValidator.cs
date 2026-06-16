using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Validation;

using DotBoxD.Kernels;

internal static class PluginManifestCapabilityValidator
{
    public static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        IReadOnlyList<string> entrypoints,
        List<SandboxDiagnostic> diagnostics)
    {
        var declared = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        var expected = RequiredCapabilities(plan, entrypoints);
        if (declared.Count == manifest.RequiredCapabilities.Count &&
            declared.SetEquals(expected))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "DBXK044",
            "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities."));
    }

    private static HashSet<string> RequiredCapabilities(ExecutionPlan plan, IReadOnlyList<string> entrypoints)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrypoint in entrypoints)
        {
            if (!plan.BindingReferences.TryGetValue(entrypoint, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (!plan.Bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                if (binding.RequiredCapability is not null)
                {
                    required.Add(binding.RequiredCapability);
                }

                if (binding.IsAsync)
                {
                    required.Add(RuntimeCapabilityIds.Async);
                }
            }
        }

        return required;
    }
}
