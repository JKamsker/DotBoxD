using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyStreamSetupEmitter
{
    public static (
        string? ArrayName,
        System.Collections.Generic.Dictionary<int, string> Handles,
        System.Collections.Generic.List<(string HandleName, string ReservedName)>? Reservations) Emit(
        StringBuilder sb,
        System.Collections.Generic.List<ParameterModel> requestParameters,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        var handles = new System.Collections.Generic.Dictionary<int, string>();
        var streamCount = CountStreamedParameters(requestParameters, ct);
        if (streamCount == 0)
        {
            return (null, handles, null);
        }

        var arrayName = locals.Reserve("__sharpc_streams", ct);
        var reservations =
            new System.Collections.Generic.List<(string HandleName, string ReservedName, string Kind, string AttachmentExpression)>(streamCount);
        var reservationFlags = new System.Collections.Generic.List<(string HandleName, string ReservedName)>(streamCount);

        for (var i = 0; i < requestParameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var parameter = requestParameters[i];
            if (parameter.StreamKind == ParameterStreamKind.None)
            {
                continue;
            }

            var handleName = locals.Reserve("__sharpc_stream" + (i + 1), ct);
            var reservedName = locals.Reserve(handleName + "Reserved", ct);
            handles[i] = handleName;
            reservations.Add((
                handleName,
                reservedName,
                parameter.StreamKind == ParameterStreamKind.AsyncEnumerable ? "Items" : "Binary",
                BuildAttachmentExpression(parameter, handleName)));
            reservationFlags.Add((handleName, reservedName));

            sb.AppendLine($"{indent}global::ShaRPC.Core.Protocol.RpcStreamHandle {handleName} = default;");
            sb.AppendLine($"{indent}var {reservedName} = false;");
        }

        EmitReservationBlock(sb, reservations, ct, indent, arrayName);
        return (arrayName, handles, reservationFlags);
    }

    private static int CountStreamedParameters(
        System.Collections.Generic.List<ParameterModel> requestParameters,
        CancellationToken ct)
    {
        var count = 0;
        foreach (var parameter in requestParameters)
        {
            ct.ThrowIfCancellationRequested();
            if (parameter.StreamKind != ParameterStreamKind.None)
            {
                count++;
            }
        }

        return count;
    }

    private static void EmitReservationBlock(
        StringBuilder sb,
        System.Collections.Generic.List<(string HandleName, string ReservedName, string Kind, string AttachmentExpression)> reservations,
        CancellationToken ct,
        string indent,
        string arrayName)
    {
        sb.AppendLine($"{indent}global::ShaRPC.Core.Streaming.RpcStreamAttachment[] {arrayName};");
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        foreach (var reservation in reservations)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine($"{indent}    {reservation.HandleName} = this._invoker.ReserveStream(global::ShaRPC.Core.Protocol.RpcStreamKind.{reservation.Kind});");
            sb.AppendLine($"{indent}    {reservation.ReservedName} = true;");
        }

        sb.AppendLine($"{indent}    {arrayName} = new global::ShaRPC.Core.Streaming.RpcStreamAttachment[]");
        sb.AppendLine($"{indent}    {{");
        foreach (var reservation in reservations)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine($"{indent}        {reservation.AttachmentExpression},");
        }

        sb.AppendLine($"{indent}    }};");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch");
        sb.AppendLine($"{indent}{{");
        for (var i = reservations.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var reservation = reservations[i];
            sb.AppendLine($"{indent}    if ({reservation.ReservedName})");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        this._invoker.ReleaseStream({reservation.HandleName});");
            sb.AppendLine($"{indent}    }}");
        }

        sb.AppendLine($"{indent}    throw;");
        sb.AppendLine($"{indent}}}");
    }

    private static string BuildAttachmentExpression(ParameterModel parameter, string handleName) =>
        parameter.StreamKind switch
        {
            ParameterStreamKind.Stream =>
                $"global::ShaRPC.Core.Streaming.RpcStreamAttachment.FromStream({handleName}, {parameter.Name})",
            ParameterStreamKind.Pipe =>
                $"global::ShaRPC.Core.Streaming.RpcStreamAttachment.FromPipe({handleName}, {parameter.Name})",
            ParameterStreamKind.AsyncEnumerable =>
                $"global::ShaRPC.Core.Streaming.RpcStreamAttachment.FromAsyncEnumerable<{parameter.StreamItemType}>({handleName}, {parameter.Name})",
            _ => throw new System.InvalidOperationException("Parameter is not streamed."),
        };
}
