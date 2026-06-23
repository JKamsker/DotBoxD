namespace DotBoxD.Kernels.Game.Server.Abstractions;

/// <summary>
/// Server-owned hook context contract that plugins can consume without any DotBoxD core-library changes.
/// The sample keeps it in the server abstractions assembly because that is the shared contract the plugin
/// already references; the server implementation project remains private to the host process.
/// </summary>
public sealed class GamePluginContext
{
    private readonly HookContext _raw;

    public GamePluginContext(HookContext raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _raw = raw;
    }

    public IPluginMessageSink Messages => _raw.Messages;

    public CancellationToken CancellationToken => _raw.CancellationToken;

    public bool HasCancelableDispatch => _raw.CancellationToken.CanBeCanceled;

    public string DamageDecisionReason => "remote";

    public string FormatCalmTarget(string monsterId) => "ctx:" + monsterId;

    public int ScaleDamageDecision(int damage) => damage * 2;

    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);
}
