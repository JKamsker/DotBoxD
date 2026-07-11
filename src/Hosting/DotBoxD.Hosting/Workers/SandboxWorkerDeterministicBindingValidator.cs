using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class SandboxWorkerDeterministicBindingValidator
{
    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionResult result)
    {
        if (!result.Succeeded ||
            !DirectlyReturnsTimeNowUnixMillis(plan, entrypoint))
        {
            return true;
        }

        return result.Value is I64Value returned &&
               TryGetSingleTimeNowUnixMillisAuditValue(result, out var audited) &&
               returned.Value == audited;
    }

    private static bool DirectlyReturnsTimeNowUnixMillis(
        ExecutionPlan plan,
        string entrypoint)
        => plan.FunctionLookup.TryGetValue(entrypoint, out var function) &&
           function.Body.Count == 1 &&
           function.Body[0] is ReturnStatement
           {
               Value: CallExpression call
           } &&
           call.Name == SafeTimeBindingNames.NowUnixMillisId &&
           call.Arguments.Count == 0 &&
           call.GenericType is null;

    private static bool TryGetSingleTimeNowUnixMillisAuditValue(
        SandboxExecutionResult result,
        out long value)
    {
        value = 0;
        var matched = false;
        foreach (var auditEvent in result.AuditEvents)
        {
            if (auditEvent.Kind != BindingAuditKinds.BindingCall ||
                auditEvent.BindingId != SafeTimeBindingNames.NowUnixMillisId ||
                !auditEvent.Success)
            {
                continue;
            }

            if (matched ||
                auditEvent.Fields is null ||
                !auditEvent.Fields.TryGetValue(SafeTimeBindingNames.NowUnixMillisAuditField, out var raw) ||
                !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }
}
