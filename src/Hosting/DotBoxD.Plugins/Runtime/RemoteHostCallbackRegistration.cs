namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Describes a generated remote filter package plus the native callback that should run after the host
/// transport accepts an event for that package.
/// </summary>
public sealed record RemoteHostCallbackRegistration(
    Type EventType,
    PluginPackage Package,
    Delegate Handler);
