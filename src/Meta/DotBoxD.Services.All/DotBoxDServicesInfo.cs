namespace DotBoxD.Services.All;

/// <summary>
/// Marker type for the <c>DotBoxD.Services.All</c> meta-package. The package carries no logic; it
/// bundles the service-only stack — the source-generated RPC core (<c>DotBoxD.Services</c>), the
/// MessagePack codec (<c>DotBoxD.Codecs.MessagePack</c>), and the TCP and named-pipe transports
/// (<c>DotBoxD.Transports.Tcp</c>, <c>DotBoxD.Transports.NamedPipes</c>). It targets
/// <c>netstandard2.1</c> and is Unity/IL2CPP compatible.
/// </summary>
public static class DotBoxDServicesInfo
{
    /// <summary>The components bundled by this service/channels meta-package.</summary>
    public const string Components =
        "DotBoxD.Services, DotBoxD.Codecs.MessagePack, DotBoxD.Transports.Tcp, DotBoxD.Transports.NamedPipes";
}
