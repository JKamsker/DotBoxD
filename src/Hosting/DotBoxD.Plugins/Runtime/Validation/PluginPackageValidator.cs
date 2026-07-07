using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime.Lifecycle;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginPackageValidator
{
    public static void Validate(PluginPackage package)
    {
        PluginPackageRootValidator.Validate(package);
        var diagnostics = new List<SandboxDiagnostic>();
        PluginPackageValidationPhases.ValidateManifestIdentity(package, diagnostics);
        var metadataKernel = ValidateModuleKernelMetadata(package, diagnostics);
        PluginPackageValidationPhases.ValidateManifestMode(package.Manifest, diagnostics);
        var hasModuleCollectionErrors = PluginModuleCollectionValidator.Validate(package.Module, diagnostics);
        PluginPackageValidationPhases.ValidateRpcEntrypoint(package.Manifest, diagnostics);
        if (hasModuleCollectionErrors)
        {
            ThrowIfErrors(diagnostics);
        }

        PluginPackageValidationPhases.ValidateManifestDetails(package, diagnostics);
        PluginPackageValidationPhases.ValidateLiveSettings(package.Manifest.LiveSettings, diagnostics);
        PluginPackageValidationPhases.ValidateSubscriptions(package.Manifest.Subscriptions, metadataKernel, diagnostics);
        ThrowIfErrors(diagnostics);
    }

    public static void ValidatePrepared(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events,
        SandboxPolicy installPolicy)
        => PluginPreparedPackageValidator.Validate(package, plan, events, installPolicy, PluginManifestEffectValidator.Validate);

    private static string? ValidateModuleKernelMetadata(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.Kernel, out var metadataKernel) ||
            string.IsNullOrWhiteSpace(metadataKernel))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK013", "Plugin module metadata must bind to the manifest kernel."));
            return null;
        }

        PluginManifestTextValidator.ValidateText(metadataKernel, "kernel metadata", diagnostics);
        return metadataKernel;
    }

    internal static void ValidateRequiredCapabilities(
        PluginManifest manifest,
        List<SandboxDiagnostic> diagnostics)
    {
        if (manifest.RequiredCapabilities.Any(static capability => capability is null))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK045",
                "Plugin manifest requiredCapabilities must not contain null entries."));
        }
    }

    internal static void ValidateResultMetadata(
        HookSubscriptionManifest subscription,
        List<SandboxDiagnostic> diagnostics)
    {
        if (subscription.ProjectedType is not null)
        {
            PluginManifestTextValidator.ValidateText(subscription.ProjectedType, "hook projected type", diagnostics);
            if (!subscription.LocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "A hook subscription that declares projectedType must also declare localTerminal."));
            }
        }

        if (subscription.ResultType is null)
        {
            if (subscription.LocalTerminal && subscription.ProjectedType is null)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must declare an explicit projected type."));
            }

            if (subscription.ResultLocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "A result-local hook subscription must declare resultType."));
            }

            return;
        }

        PluginManifestTextValidator.ValidateText(subscription.ResultType, "hook result type", diagnostics);
        if (subscription.LocalTerminal || subscription.ProjectedType is not null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK031",
                "A hook subscription cannot combine result hook metadata with RunLocal projection metadata."));
        }
    }

    internal static void ValidateEntrypoints(
        PluginPackage package,
        PluginEntrypointIndex entrypointIndex,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateEntrypoint(
            entrypointIndex,
            package.Entrypoints.ShouldHandle,
            PluginManifestNames.Entrypoints.ShouldHandle,
            diagnostics);
        ValidateEntrypoint(
            entrypointIndex,
            package.Entrypoints.Handle,
            PluginManifestNames.Entrypoints.Handle,
            diagnostics);
    }

    private static void ValidateEntrypoint(
        PluginEntrypointIndex entrypointIndex,
        string functionId,
        string name,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(functionId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK032", $"Kernel {name} entrypoint is required."));
            return;
        }

        PluginManifestTextValidator.ValidateText(functionId, $"kernel {name} entrypoint", diagnostics);

        if (!entrypointIndex.Contains(functionId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public."));
        }
    }

    internal static void ValidateSetting(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try
        {
            LiveSettingTypeConverter.ValidateDefinition(setting);
        }
        catch (SandboxValidationException ex)
        {
            diagnostics.AddRange(ex.Diagnostics);
        }
        catch (Exception)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK020", $"Live setting type '{setting.Type}' is not supported."));
        }
    }

    internal static void ThrowIfErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }
}
