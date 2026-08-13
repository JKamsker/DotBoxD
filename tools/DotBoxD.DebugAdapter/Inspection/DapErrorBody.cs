namespace DotBoxD.DebugAdapter;

internal static class DapErrorBody
{
    private const int DebugAdapterErrorId = 1;

    public static object Create(DebugAdapterException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new
        {
            error = new
            {
                id = DebugAdapterErrorId,
                format = $"[{exception.Code}] {exception.Message}"
            }
        };
    }
}
