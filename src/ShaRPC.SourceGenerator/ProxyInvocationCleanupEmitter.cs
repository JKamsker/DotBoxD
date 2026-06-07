using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyInvocationCleanupEmitter
{
    public static void EmitProxyInvocation(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        System.Collections.Generic.List<(string HandleName, string ReservedName)>? reservations,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent = "            ")
    {
        if (reservations is not { Count: > 0 })
        {
            ProxyInvocationEmitter.Emit(sb, method, invocation, locals, ct, indent);
            return;
        }

        var returnedName = locals.Reserve("__sharpc_invocationReturned", ct);
        var trackName = locals.Reserve("__sharpc_trackInvocation", ct);
        EmitTracker(sb, returnedName, trackName, locals.Reserve("__sharpc_value", ct), indent);
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        ProxyInvocationEmitter.Emit(sb, method, $"{trackName}({invocation})", locals, ct, indent + "    ");
        sb.AppendLine($"{indent}}}");
        EmitReservationCleanupCatch(sb, returnedName, reservations, ct, indent);
    }

    public static void EmitInvocationAssignment(
        StringBuilder sb,
        string targetType,
        string targetName,
        string invocation,
        System.Collections.Generic.List<(string HandleName, string ReservedName)>? reservations,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        if (reservations is not { Count: > 0 })
        {
            sb.AppendLine($"{indent}{targetType} {targetName} = {invocation};");
            return;
        }

        var returnedName = locals.Reserve("__sharpc_invocationReturned", ct);
        var trackName = locals.Reserve("__sharpc_trackInvocation", ct);
        EmitTracker(sb, returnedName, trackName, locals.Reserve("__sharpc_value", ct), indent);
        sb.AppendLine($"{indent}{targetType} {targetName};");
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    {targetName} = {trackName}({invocation});");
        sb.AppendLine($"{indent}}}");
        EmitReservationCleanupCatch(sb, returnedName, reservations, ct, indent);
    }

    private static void EmitTracker(
        StringBuilder sb,
        string returnedName,
        string trackName,
        string valueName,
        string indent)
    {
        sb.AppendLine($"{indent}var {returnedName} = false;");
        sb.AppendLine($"{indent}T {trackName}<T>(T {valueName})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    {returnedName} = true;");
        sb.AppendLine($"{indent}    return {valueName};");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitReservationCleanupCatch(
        StringBuilder sb,
        string returnedName,
        System.Collections.Generic.List<(string HandleName, string ReservedName)> reservations,
        CancellationToken ct,
        string indent)
    {
        sb.AppendLine($"{indent}catch");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if (!{returnedName})");
        sb.AppendLine($"{indent}    {{");
        for (var i = reservations.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var reservation = reservations[i];
            sb.AppendLine($"{indent}        if ({reservation.ReservedName})");
            sb.AppendLine($"{indent}        {{");
            sb.AppendLine($"{indent}            this._invoker.ReleaseStream({reservation.HandleName});");
            sb.AppendLine($"{indent}        }}");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}    throw;");
        sb.AppendLine($"{indent}}}");
    }
}
