using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core;

internal readonly record struct RpcError(string Message, string Type);

internal static class RpcErrors
{
    public const int MaxReflectedValueLength = 256;

    public static RpcError FromException(Exception exception) =>
        exception is ShaRpcException
            ? new RpcError(Truncate(exception.Message), exception.GetType().Name)
            : new RpcError("Internal error.", "ShaRpcInternalError");

    public static RpcError ServiceNotFound(string serviceName) =>
        new(
            $"Service '{Truncate(serviceName)}' not found.",
            nameof(ShaRpcNotFoundException));

    public static RpcError Protocol(string message) =>
        new(message, nameof(ShaRpcProtocolException));

    public static string Truncate(string value)
    {
        if (value.Length <= MaxReflectedValueLength)
        {
            return value;
        }

        return value.Substring(0, MaxReflectedValueLength - 3) + "...";
    }
}
