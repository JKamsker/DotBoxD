namespace DotBoxD.Plugins.Debugging;

/// <summary>Version-one command names understood by <see cref="PluginDebugSession"/>.</summary>
public static class PluginDebugCommands
{
    public const string Initialize = "initialize";
    public const string Attach = "attach";
    public const string SetBreakpoints = "setBreakpoints";
    public const string Pause = "pause";
    public const string Continue = "continue";
    public const string StepIn = "stepIn";
    public const string StepOver = "stepOver";
    public const string StepOut = "stepOut";
    public const string Threads = "threads";
    public const string StackTrace = "stackTrace";
    public const string Variables = "variables";
    public const string Completions = "completions";
    public const string SetVariable = "setVariable";
    public const string Evaluate = "evaluate";
    public const string SetExpression = "setExpression";
    public const string UploadAssembly = "uploadAssembly";
    public const string Heartbeat = "heartbeat";
    public const string Disconnect = "disconnect";
}
