using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingClassificationValidator
{
    internal static void Validate(
        string bindingId,
        AuditLevel auditLevel,
        string? auditKind,
        BindingSafety safety,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!IsKnownAuditLevel(auditLevel))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-AUDIT", $"binding '{bindingId}' declares an unknown audit level"));
        }

        if (!IsKnownAuditKind(auditKind))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-AUDIT", $"binding '{bindingId}' declares an unknown audit kind"));
        }

        if (!IsKnownBindingSafety(safety))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-SAFETY", $"binding '{bindingId}' declares an unknown safety classification"));
        }
    }

    private static bool IsKnownAuditLevel(AuditLevel auditLevel)
        => auditLevel is AuditLevel.None or
            AuditLevel.Summary or
            AuditLevel.PerCall or
            AuditLevel.PerResource or
            AuditLevel.FullInputOutput;

    private static bool IsKnownAuditKind(string? auditKind)
        => auditKind is BindingAuditKinds.BindingCall or BindingAuditKinds.SandboxLog or BindingAuditKinds.PluginMessage;

    private static bool IsKnownBindingSafety(BindingSafety safety)
        => safety is BindingSafety.PureIntrinsic or
            BindingSafety.PureHostFacade or
            BindingSafety.ReadOnlyExternal or
            BindingSafety.SideEffectingExternal or
            BindingSafety.DangerousRequiresReview;
}
