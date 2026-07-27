using System.Threading.Tasks.Sources;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Send.Pooling;

internal sealed class TestPooledFrameSendOperation<TTag> :
    PooledFrameSendOperation<TestPooledFrameSendOperation<TTag>>
{
    private static readonly Func<TestPooledFrameSendOperation<TTag>> Factory =
        static () => new TestPooledFrameSendOperation<TTag>();
    private static int s_createdCount;

    private Action<TestPooledFrameSendOperation<TTag>, ValueTask>? _resumeHandler;
    private Action<TestPooledFrameSendOperation<TTag>, Exception>? _registrationFailureHandler;
    private Exception? _cleanupError;
    private object? _retainedState;

    private TestPooledFrameSendOperation() => Interlocked.Increment(ref s_createdCount);

    public static int CreatedCount => Volatile.Read(ref s_createdCount);

    public static int RetainedCount => RetainedOperationCount;

    public static bool HasAvailable => HasAvailableOperation;

    public static TestPooledFrameSendOperation<TTag>? RentOrCreate() =>
        TryRentOrCreateOperation(Factory);

    public static TestPooledFrameSendOperation<TTag>? TryTakeRecycled() =>
        TryRentOperation();

    public int ClearCount { get; private set; }

    public int RegistrationFailureCount { get; private set; }

    public int ResumeCount { get; private set; }

    public object? RetainedState => _retainedState;

    public short ExpectedToken => CurrentToken;

    public ValueTask Issue(
        ValueTask pendingOperation,
        object? retainedState = null,
        Action<TestPooledFrameSendOperation<TTag>, ValueTask>? resumeHandler = null,
        Action<TestPooledFrameSendOperation<TTag>, Exception>? registrationFailureHandler = null,
        Exception? cleanupError = null)
    {
        _retainedState = retainedState;
        _resumeHandler = resumeHandler;
        _registrationFailureHandler = registrationFailureHandler;
        _cleanupError = cleanupError;
        return IssuePendingOperation(pendingOperation);
    }

    public ValueTask IssueWithoutExternalStateMutation(ValueTask pendingOperation) =>
        IssuePendingOperation(pendingOperation);

    public void RegisterNext(ValueTask pendingOperation) =>
        RegisterPendingOperation(pendingOperation);

    public void CompleteSuccessfully() => PublishResult();

    public void CompleteWithError(Exception error) => PublishException(error);

    public void Consume(short token) => GetResult(token);

    protected override void ResumePendingOperation(ValueTask pendingOperation)
    {
        ResumeCount++;
        if (_resumeHandler is { } handler)
        {
            handler(this, pendingOperation);
            return;
        }

        Exception? error = null;
        try
        {
            pendingOperation.GetAwaiter().GetResult();
        }
        catch (Exception caught)
        {
            error = caught;
        }

        if (error is null)
        {
            PublishResult();
        }
        else
        {
            PublishException(error);
        }
    }

    protected override void HandlePendingRegistrationFailure(Exception error)
    {
        RegistrationFailureCount++;
        if (_registrationFailureHandler is { } handler)
        {
            handler(this, error);
            return;
        }

        PublishException(error);
    }

    protected override void ClearExternalState()
    {
        var cleanupError = _cleanupError;
        _retainedState = null;
        _resumeHandler = null;
        _registrationFailureHandler = null;
        _cleanupError = null;
        ClearCount++;
        if (cleanupError is not null)
        {
            throw cleanupError;
        }
    }
}

internal sealed class ControlledPendingSend : IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> _source;
    private int _getResultCount;

    public ValueTask Pending => new(this, _source.Version);

    public int GetResultCount => Volatile.Read(ref _getResultCount);

    public void Succeed() => _source.SetResult(true);

    public void Fail(Exception error) => _source.SetException(error);

    public void GetResult(short token)
    {
        Interlocked.Increment(ref _getResultCount);
        _source.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);
}

internal sealed class ThrowingRegistrationSend(Exception error) : IValueTaskSource
{
    public ValueTask Pending => new(this, token: 0);

    public void GetResult(short token) => throw new InvalidOperationException(
        "The unregistered operation cannot be consumed.");

    public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Pending;

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) => throw error;
}

// Intentionally nonconforming: this proves a delayed registration throw cannot corrupt a newer
// lease. Arbitrary sources that invoke a continuation more than once remain outside the contract.
internal sealed class InlineCompletionThenThrowingSend(Exception error) : IValueTaskSource
{
    private readonly ManualResetEventSlim _allowThrow = new(initialState: false);
    private readonly TaskCompletionSource _continuationReturned =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    public ValueTask Pending => new(this, token: 0);

    public Task ContinuationReturned => _continuationReturned.Task;

    public void AllowThrow() => _allowThrow.Set();

    public void GetResult(short token)
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            throw new InvalidOperationException("The inline operation has not completed.");
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) =>
        Volatile.Read(ref _completed) == 0
            ? ValueTaskSourceStatus.Pending
            : ValueTaskSourceStatus.Succeeded;

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        Volatile.Write(ref _completed, 1);
        continuation(state);
        _continuationReturned.TrySetResult();
        if (!_allowThrow.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The stale registration failure was not released.");
        }

        throw error;
    }
}
