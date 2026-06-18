using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Input;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime;

internal static class KernelEntrypointValidator
{
    public static void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
    {
        Validate(manifest, plan, entrypoints, PluginEventShape.From(adapter));
    }

    public static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape)
    {
        if (!manifest.Subscriptions.Any(s => EventNameMatch.Matches(s.Event, adapterShape.EventName)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapterShape.EventName}'.")
            ]);
        }

        var expected = PluginParameterShape.BuildExpected(adapterShape.Parameters, manifest.LiveSettings);
        ValidateFunction(plan, entrypoints.ShouldHandle, SandboxType.Bool, expected);
        ValidateFunction(plan, entrypoints.Handle, HandleReturnType(manifest), expected);
    }

    // An ordinary chain's Handle returns Unit (it performs a host send). A local-terminal (RunLocal) chain's
    // Handle returns the projected value the host pushes to the plugin, so its expected return type is the
    // subscription's declared ProjectedType.
    private static SandboxType HandleReturnType(PluginManifest manifest)
    {
        foreach (var subscription in manifest.Subscriptions)
        {
            if (subscription.LocalTerminal && subscription.ProjectedType is { } projectedType)
            {
                return LiveSettingTypeConverter.ToSandboxType(projectedType);
            }
        }

        return SandboxType.Unit;
    }

    private static void ValidateFunction(
        ExecutionPlan plan,
        string functionId,
        SandboxType returnType,
        IReadOnlyList<Parameter> expected)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        if (function.ReturnType != returnType ||
            !PluginParameterShape.Matches(function.Parameters, expected))
        {
            throw SignatureError(functionId);
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("DBXK033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
