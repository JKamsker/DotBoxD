using System.Threading.Tasks.Sources;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

internal sealed class TestPooledFrameReceiveOperation<TTag>
    : PooledFrameReceiveOperation<TestPooledFrameReceiveOperation<TTag>>
{
    private object? _retainedState;
    private short _expectedVersion;
    private int _clearCount;

    public object? RetainedState => _retainedState;

    public short ExpectedVersion => _expectedVersion;

    public int ClearCount => Volatile.Read(ref _clearCount);

    public static TestPooledFrameReceiveOperation<TTag> Rent() =>
        TryRentOperation() ?? new TestPooledFrameReceiveOperation<TTag>();

    public static TestPooledFrameReceiveOperation<TTag>? TryTakeRecycled() =>
        TryRentOperation();

    public ValueTask<RpcFrame> Issue(object? retainedState = null)
    {
        _retainedState = retainedState;
        return IssueValueTask();
    }

    public void Succeed() => PublishResult(new RpcFrame(Payload.Empty));

    public void Fail(Exception error) => PublishException(error);

    public RpcFrame Consume(short token) =>
        ((IValueTaskSource<RpcFrame>)this).GetResult(token);

    protected override void ClearForRecycle()
    {
        _retainedState = null;
        _expectedVersion = unchecked((short)(_expectedVersion + 1));
        Interlocked.Increment(ref _clearCount);
    }
}
