using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginPackageValidationPhases
{
    public static void ValidateManifestIdentity(
        PluginPackage package,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(package.Manifest.PluginId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK010", "Plugin id is required."));
        }

        PluginManifestTextValidator.ValidatePluginId(package.Manifest.PluginId, diagnostics);
        PluginManifestTextValidator.ValidateText(package.Manifest.Contract, "plugin contract", diagnostics);
        ValidateManifestModuleId(package, diagnostics);
        ValidateMetadataPluginId(package, diagnostics);
    }

    public static void ValidateRpcEntrypoint(
        PluginManifest manifest,
        List<SandboxDiagnostic> diagnostics)
    {
        if (manifest.RpcEntrypoint is not null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK073",
                "Hook kernel manifests must not declare rpcEntrypoint."));
        }
    }

    public static void ValidateManifestDetails(
        PluginPackage package,
        List<SandboxDiagnostic> diagnostics)
    {
        PluginManifestEffectValidator.Validate(package.Manifest, diagnostics);
        PluginPackageValidator.ValidateRequiredCapabilities(package.Manifest, diagnostics);
        PluginManifestCapabilityValidator.ValidateConcreteRequiredCapabilityEntries(
            package.Manifest,
            package.Module,
            diagnostics);
        PluginPackageValidator.ValidateEntrypoints(package, PluginEntrypointIndex.Build(package), diagnostics);
    }

    public static void ValidateLiveSettings(
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!PluginManifestElementValidator.ValidateNoNullElements(liveSettings, "liveSettings", diagnostics))
        {
            return;
        }

        ValidateDuplicateLiveSettings(liveSettings, diagnostics);
        foreach (var setting in liveSettings)
        {
            ValidateLiveSetting(setting, diagnostics);
        }
    }

    public static void ValidateSubscriptions(
        IReadOnlyList<HookSubscriptionManifest> subscriptions,
        string? metadataKernel,
        List<SandboxDiagnostic> diagnostics)
    {
        var subscriptionsValid = PluginManifestElementValidator.ValidateNoNullElements(
            subscriptions,
            "subscriptions",
            diagnostics);
        ValidateSubscriptionCount(subscriptions, diagnostics);
        if (!subscriptionsValid)
        {
            PluginPackageValidator.ThrowIfErrors(diagnostics);
            return;
        }

        foreach (var subscription in subscriptions)
        {
            ValidateSubscription(subscription, metadataKernel, diagnostics);
        }
    }

    public static void ValidateManifestMode(PluginManifest manifest, List<SandboxDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(manifest.Mode))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK042", "Plugin manifest execution mode is not supported."));
        }
    }

    private static void ValidateManifestModuleId(
        PluginPackage package,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!string.Equals(package.Manifest.PluginId, package.Module.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK011", "Plugin manifest id must match module id."));
        }
    }

    private static void ValidateMetadataPluginId(
        PluginPackage package,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.PluginId, out var metadataPluginId) ||
            !string.Equals(metadataPluginId, package.Manifest.PluginId, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK012", "Plugin module metadata must bind to the manifest plugin id."));
        }
    }

    private static void ValidateDuplicateLiveSettings(
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var group in liveSettings.GroupBy(static s => s.Name, StringComparer.Ordinal))
        {
            if (group.Skip(1).Any())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK021", $"Live setting '{group.Key}' is declared more than once."));
            }
        }
    }

    private static void ValidateLiveSetting(
        LiveSettingDefinition setting,
        List<SandboxDiagnostic> diagnostics)
    {
        PluginManifestTextValidator.ValidateText(setting.Name, "live setting name", diagnostics);
        PluginManifestTextValidator.ValidateText(setting.Type, "live setting type", diagnostics);
        PluginPackageValidator.ValidateSetting(setting, diagnostics);
    }

    private static void ValidateSubscriptionCount(
        IReadOnlyList<HookSubscriptionManifest> subscriptions,
        List<SandboxDiagnostic> diagnostics)
    {
        if (subscriptions.Count == 0)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK030", "At least one hook subscription is required."));
            return;
        }

        if (subscriptions.Count > 1)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK031",
                "A plugin package must declare exactly one hook subscription."));
        }
    }

    private static void ValidateSubscription(
        HookSubscriptionManifest subscription,
        string? metadataKernel,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateSubscriptionShape(subscription, diagnostics);
        PluginPackageValidator.ValidateResultMetadata(subscription, diagnostics);
        ValidateSubscriptionKernelMetadata(subscription, metadataKernel, diagnostics);
        PluginManifestPredicateValidator.ValidateIndexedPredicates(subscription, diagnostics);
    }

    private static void ValidateSubscriptionShape(
        HookSubscriptionManifest subscription,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(subscription.Event) || string.IsNullOrWhiteSpace(subscription.Kernel))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK031", "Hook subscription event and kernel are required."));
        }

        PluginManifestTextValidator.ValidateText(subscription.Event, "hook subscription event", diagnostics);
        PluginManifestTextValidator.ValidateText(subscription.Kernel, "hook subscription kernel", diagnostics);
    }

    private static void ValidateSubscriptionKernelMetadata(
        HookSubscriptionManifest subscription,
        string? metadataKernel,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(metadataKernel) &&
            !string.Equals(subscription.Kernel, metadataKernel, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK013",
                $"Hook subscription kernel '{subscription.Kernel}' must match module kernel '{metadataKernel}'."));
        }
    }
}
