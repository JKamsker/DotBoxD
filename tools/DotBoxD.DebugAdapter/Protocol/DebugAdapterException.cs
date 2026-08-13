namespace DotBoxD.DebugAdapter;

internal sealed class DebugAdapterException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
