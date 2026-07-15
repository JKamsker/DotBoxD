namespace DotBoxD.Plugins.Debugging;

/// <summary>Describes a rejected remote-debug wire message without exposing server internals.</summary>
public sealed class PluginDebugProtocolException : Exception
{
    public PluginDebugProtocolException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
    }

    public string Code { get; }
}
