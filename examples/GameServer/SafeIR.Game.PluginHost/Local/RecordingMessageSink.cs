namespace SafeIR.Game.PluginHost;

/// <summary>
/// Records the commands a kernel emits so the local preview can show filter decisions and projected
/// payloads before anything is shipped to the server.
/// </summary>
internal sealed class RecordingMessageSink : IPluginMessageSink
{
    private readonly List<PluginMessage> _messages = [];

    public IReadOnlyList<PluginMessage> Messages => _messages;

    public void Send(string targetId, string message)
        => _messages.Add(new PluginMessage(targetId, message));

    public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Add(new PluginMessage(targetId, message));
        return ValueTask.CompletedTask;
    }
}
