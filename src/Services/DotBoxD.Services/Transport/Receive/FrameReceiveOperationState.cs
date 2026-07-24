namespace DotBoxD.Services.Transport;

/// <summary>Mutable frame-read state that moves from the initiating stack to a pooled operation.</summary>
internal struct FrameReceiveOperationState
{
    public StreamFrameReceiveOwner Owner;
    public CancellationToken CallerToken;
    public CancellationToken ReadToken;
    public int Remaining;
    public bool PhaseStarted;
    public bool WriterBacked;
}
