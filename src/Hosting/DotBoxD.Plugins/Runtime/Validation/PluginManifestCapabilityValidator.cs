using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginManifestCapabilityValidator
{
    public static void Validate(
        PluginManifest manifest,
        SandboxModule module,
        ExecutionPlan plan,
        IReadOnlyList<string> entrypoints,
        List<SandboxDiagnostic> diagnostics,
        bool allowNonBindingCapabilities = true,
        bool includeModuleNonBindingCapabilities = true,
        bool includeModuleCapabilityRequests = true)
    {
        var declared = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        var expected = RequiredCapabilities(plan, entrypoints, includeModuleCapabilityRequests);
        AddModuleNonBindingRequiredCapabilities(module, expected, includeModuleNonBindingCapabilities);
        var missing = expected
            .Except(declared, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var extra = declared
            .Except(expected, StringComparer.Ordinal)
            .Where(capability => !allowNonBindingCapabilities || !IsKnownNonBindingCapability(capability))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (declared.Count == manifest.RequiredCapabilities.Count &&
            missing.Length == 0 &&
            extra.Length == 0)
        {
            return;
        }

        var details = new List<string>();
        if (missing.Length > 0)
        {
            details.Add("missing: " + string.Join(", ", missing));
        }

        if (extra.Length > 0)
        {
            details.Add("extra: " + string.Join(", ", extra));
        }

        if (declared.Count != manifest.RequiredCapabilities.Count)
        {
            details.Add("duplicates present");
        }

        var message = details.Count == 0
            ? "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities."
            : "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities (" +
              string.Join("; ", details) +
              ").";
        diagnostics.Add(new SandboxDiagnostic(
            "DBXK044",
            message));
    }

    public static IEnumerable<string> ModuleNonBindingRequiredCapabilities(SandboxModule module)
    {
        if (!module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.RequiredCapabilities, out var metadata) ||
            string.IsNullOrWhiteSpace(metadata))
        {
            yield break;
        }

        foreach (var capability in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsKnownNonBindingCapability(capability))
            {
                yield return capability;
            }
        }
    }

    public static IEnumerable<string> NonBindingRequiredCapabilities(PluginManifest manifest, SandboxModule module)
        => manifest.RequiredCapabilities
            .Concat(ModuleNonBindingRequiredCapabilities(module))
            .Where(IsKnownNonBindingCapability);

    public static void ValidateConcreteRequiredCapabilityEntries(
        PluginManifest manifest,
        SandboxModule module,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var capability in manifest.RequiredCapabilities)
        {
            AddWildcardRequiredCapabilityDiagnostic("Plugin manifest", capability, diagnostics);
        }

        if (!module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.RequiredCapabilities, out var metadata) ||
            string.IsNullOrWhiteSpace(metadata))
        {
            return;
        }

        foreach (var capability in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddWildcardRequiredCapabilityDiagnostic("Plugin module metadata", capability, diagnostics);
        }
    }

    public static void ValidateRequiredCapabilityGrants(
        PluginManifest manifest,
        SandboxModule module,
        SandboxPolicy installPolicy,
        List<SandboxDiagnostic> diagnostics,
        bool includeModuleNonBindingCapabilities = true)
    {
        PluginPackageValidator.ValidateRequiredCapabilities(manifest, diagnostics);
        var now = installPolicy.GrantClock;
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in manifest.RequiredCapabilities)
        {
            if (capability is not null)
            {
                required.Add(capability);
            }
        }

        AddModuleNonBindingRequiredCapabilities(module, required, includeModuleNonBindingCapabilities);
        foreach (var capability in required)
        {
            if (string.Equals(capability, RuntimeCapabilityIds.Async, StringComparison.Ordinal) ||
                installPolicy.GrantsCapability(capability, now))
            {
                continue;
            }

            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-CAP",
                $"required capability '{capability}' is not granted"));
        }
    }

    private static HashSet<string> RequiredCapabilities(
        ExecutionPlan plan,
        IReadOnlyList<string> entrypoints,
        bool includeModuleCapabilityRequests)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (includeModuleCapabilityRequests)
        {
            AddModuleCapabilityRequests(plan.Module, required);
        }

        foreach (var entrypoint in entrypoints)
        {
            AddEntrypointBindingCapabilities(plan, entrypoint, required);
        }

        return required;
    }

    private static void AddModuleCapabilityRequests(SandboxModule module, HashSet<string> capabilities)
    {
        foreach (var request in module.CapabilityRequests)
        {
            capabilities.Add(request.Id);
        }
    }

    private static void AddEntrypointBindingCapabilities(
        ExecutionPlan plan,
        string entrypoint,
        HashSet<string> capabilities)
    {
        if (!plan.BindingReferences.TryGetValue(entrypoint, out var bindingReferences))
        {
            return;
        }

        foreach (var bindingId in bindingReferences)
        {
            if (!plan.Bindings.TryGet(bindingId, out var binding))
            {
                continue;
            }

            if (binding.RequiredCapability is not null)
            {
                capabilities.Add(binding.RequiredCapability);
            }

            if (binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0)
            {
                capabilities.Add(RuntimeCapabilityIds.Async);
            }
        }
    }

    private static void AddModuleNonBindingRequiredCapabilities(
        SandboxModule module,
        HashSet<string> capabilities,
        bool include)
    {
        if (!include)
        {
            return;
        }

        foreach (var capability in ModuleNonBindingRequiredCapabilities(module))
        {
            capabilities.Add(capability);
        }
    }

    public static bool IsKnownNonBindingCapability(string? capability)
        => capability is not null &&
           capability.Length > "event.read.".Length &&
           capability.StartsWith("event.read.", StringComparison.Ordinal) &&
           !CapabilityPattern.IsWildcard(capability);

    private static void AddWildcardRequiredCapabilityDiagnostic(
        string source,
        string? capability,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(capability))
        {
            return;
        }

        if (!CapabilityPattern.IsWildcard(capability))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "DBXK051",
            source + " requiredCapabilities must contain concrete capability ids; " +
            $"wildcard required capability '{capability}' is not allowed."));
    }
}
