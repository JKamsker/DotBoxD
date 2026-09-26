namespace DotBoxD.Services.Protocol;

/// <summary>Public inputs to optional connection negotiation, before any RPC frame is sent.</summary>
public sealed record RpcProtocolOffer
{
    /// <summary>The connection protocol. Version 1 keeps the legacy nine-byte frame layout.</summary>
    public const ushort CurrentVersion = 1;

    public ushort MinimumVersion { get; init; } = CurrentVersion;
    public ushort MaximumVersion { get; init; } = CurrentVersion;
    /// <summary>Application-assigned codec identifier; 1 is DotBoxD MessagePack.</summary>
    public uint CodecId { get; init; } = 1;
    public ulong SupportedFeatures { get; init; }
    public ulong RequiredFeatures { get; init; }
    public int MaximumFrameSize { get; init; } = MessageFramer.MaxMessageSize;
    /// <summary>Optional 64-character SHA-256 hex contract fingerprint. If specified, both must match.</summary>
    public string? ContractFingerprint { get; init; }
}

/// <summary>Agreed connection settings. Apply the frame limit when constructing the channel.</summary>
public sealed record RpcProtocolAgreement(ushort Version, uint CodecId, ulong Features, int MaximumFrameSize);
