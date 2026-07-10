namespace DotBoxD.Plugins.Debugging;

/// <summary>Version-one command names understood by <see cref="PluginDebugSession"/>.</summary>
public static class PluginDebugCommands
{
    public const string Initialize = "initialize";
    public const string Attach = "attach";
    public const string SetBreakpoints = "setBreakpoints";
    public const string Pause = "pause";
    public const string Continue = "continue";
    public const string Heartbeat = "heartbeat";
    public const string Disconnect = "disconnect";
}
