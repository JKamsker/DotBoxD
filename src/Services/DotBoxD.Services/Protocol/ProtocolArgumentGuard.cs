namespace DotBoxD.Services.Protocol;

internal static class ProtocolArgumentGuard
{
    internal static void ThrowIfNull(object value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}
