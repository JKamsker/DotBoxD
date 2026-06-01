using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core;

internal readonly record struct RpcError(string Message, string Type);

internal static class RpcErrors
{
    public const int MaxReflectedValueLength = 256;

    public static RpcError FromException(Exception exception) =>
        exception is ShaRpcNotFoundException
            ? new RpcError("Service not found.", exception.GetType().Name)
            : exception is ShaRpcException
                ? new RpcError("Internal error.", exception.GetType().Name)
                : new RpcError("Internal error.", RpcErrorTypes.InternalError);

    public static RpcError ServiceNotFound() =>
        new(
            "Service not found.",
            nameof(ShaRpcNotFoundException));

    public static RpcError Protocol(string message) =>
        new(Truncate(message), nameof(ShaRpcProtocolException));

    public static string Truncate(string value)
    {
        if (value.Length <= MaxReflectedValueLength)
        {
            return value;
        }

        return value.Substring(0, MaxReflectedValueLength - 3) + "...";
    }
}
