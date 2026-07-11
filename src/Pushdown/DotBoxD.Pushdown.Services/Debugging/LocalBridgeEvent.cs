namespace DotBoxD.Pushdown.Services;

internal sealed record LocalBridgeEvent(object Message)
{
    public static LocalBridgeEvent Remote(byte[] payload)
        => new(new { kind = "event", payload = Convert.ToBase64String(payload) });

    public static LocalBridgeEvent SourcesChanged()
        => new(new { kind = "sourcesChanged" });
}
