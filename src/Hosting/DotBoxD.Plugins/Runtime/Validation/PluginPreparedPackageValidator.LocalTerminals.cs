using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

using DotBoxD.Kernels;

internal static partial class PluginPreparedPackageValidator
{
    private static void ValidateLocalTerminalRouting(
        IReadOnlyList<HookSubscriptionManifest> subscriptions,
        ExecutionPlan plan,
        string handleId,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateLocalTerminalShape(subscriptions, diagnostics);
        if (!plan.FunctionAnalysis.TryGetValue(handleId, out var handleAnalysis) ||
            (handleAnalysis.Effects & SandboxEffect.HostStateWrite) == 0)
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            if (subscription.LocalTerminal || subscription.ResultLocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must not declare a host-write Handle entrypoint."));
                return;
            }
        }
    }

    private static void ValidateLocalTerminalShape(
        IReadOnlyList<HookSubscriptionManifest> subscriptions,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var subscription in subscriptions)
        {
            if (subscription.LocalTerminal && subscription.ProjectedType is null)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must declare an explicit projected type."));
                return;
            }
        }
    }
}
