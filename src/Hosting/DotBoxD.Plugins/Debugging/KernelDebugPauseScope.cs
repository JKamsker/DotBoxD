namespace DotBoxD.Plugins.Debugging;

/// <summary>Defines which kernel dispatches are parked while an execution is stopped.</summary>
public enum KernelDebugPauseScope
{
    /// <summary>Park every DotBoxD kernel dispatch on the plugin server.</summary>
    Server,

    /// <summary>Park dispatches owned by the stopped execution's plugin session.</summary>
    PluginSession,

    /// <summary>Park only the stopped execution.</summary>
    Execution
}
