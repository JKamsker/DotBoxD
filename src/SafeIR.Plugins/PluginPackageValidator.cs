namespace SafeIR.Plugins;

using SafeIR;

internal static class PluginPackageValidator
{
    public static void Validate(PluginPackage package)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        if (string.IsNullOrWhiteSpace(package.Manifest.PluginId)) {
            diagnostics.Add(new SandboxDiagnostic("SGP010", "Plugin id is required."));
        }

        foreach (var group in package.Manifest.LiveSettings.GroupBy(s => s.Name, StringComparer.Ordinal)) {
            if (group.Count() > 1) {
                diagnostics.Add(new SandboxDiagnostic("SGP021", $"Live setting '{group.Key}' is declared more than once."));
            }
        }

        foreach (var setting in package.Manifest.LiveSettings) {
            ValidateSetting(setting, diagnostics);
        }

        if (package.Manifest.Subscriptions.Count == 0) {
            diagnostics.Add(new SandboxDiagnostic("SGP030", "At least one hook subscription is required."));
        }

        ThrowIfErrors(diagnostics);
    }

    private static void ValidateSetting(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try {
            _ = LiveSettingTypeConverter.ToSandboxType(setting.Type);
            _ = LiveSettingTypeConverter.ToSandboxValue(setting.Type, setting.DefaultValue);
            ValidateRange(setting, diagnostics);
        }
        catch (SandboxValidationException ex) {
            diagnostics.AddRange(ex.Diagnostics);
        }
        catch (Exception) {
            diagnostics.Add(new SandboxDiagnostic("SGP020", $"Live setting type '{setting.Type}' is not supported."));
        }
    }

    private static void ValidateRange(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try {
            LiveSettingTypeConverter.ValidateRangeDefinition(setting);
        }
        catch (SandboxValidationException ex) {
            diagnostics.AddRange(ex.Diagnostics);
        }
    }

    private static void ThrowIfErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0) {
            throw new SandboxValidationException(diagnostics);
        }
    }
}
