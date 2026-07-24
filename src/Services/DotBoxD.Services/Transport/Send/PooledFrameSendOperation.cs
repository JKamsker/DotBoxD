using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Owns a bounded reusable completion source for an owned-frame send that genuinely suspended.
/// </summary>
internal abstract class PooledFrameSendOperation<TOperation> : IValueTaskSource
    where TOperation : PooledFrameSendOperation<TOperation>
{
    private const int ProducerIdle = 0;
    private const int ProducerInitializing = 1;
    private const int ProducerActive = 2;
    private const int ProducerPublishing = 3;

    private static readonly ContextCallback ResumeInContext =
        static state => ((PooledFrameSendOperation<TOperation>)state!).ResumeCore();
    private static readonly BoundedTransportOperationPool<TOperation> Pool = new();

    private readonly Action _resume;
    private ManualResetValueTaskSourceCore<bool> _source;
    private PooledValueTaskSourceLifecycle _lifecycle;
    private ExecutionContext? _executionContext;
    private ValueTask _pendingOperation;
    private int _leaseGeneration;
    private int _producerState;

    protected PooledFrameSendOperation() => _resume = Resume;

    protected static TOperation? TryRentOperation() => Pool.Rent();

    protected static TOperation? TryRentOrCreateOperation(Func<TOperation> operationFactory)
    {
        if (operationFactory is null)
        {
            throw new ArgumentNullException(nameof(operationFactory));
        }

        var operation = Pool.Rent();
        if (operation is not null)
        {
            return operation;
        }

        if (!BoundedTransportOperationCreationBudget<TOperation>.TryReserve(out _))
        {
            // A producer may have returned an operation after the initial hot-slot miss.
            return Pool.Rent();
        }

        try
        {
            return operationFactory() ?? throw new InvalidOperationException(
                "The frame-send operation factory returned null.");
        }
        catch
        {
            BoundedTransportOperationCreationBudget<TOperation>.CancelReservation();
            throw;
        }
    }

    protected static bool HasAvailableOperation => Pool.HasAvailable;

    protected static int RetainedOperationCount => Pool.RetainedCount;

    protected short CurrentToken => _source.Version;

    /// <summary>Issues this lease and registers its first genuinely pending operation.</summary>
    protected ValueTask IssuePendingOperation(ValueTask pendingOperation)
    {
        if (Interlocked.CompareExchange(
                ref _producerState,
                ProducerInitializing,
                ProducerIdle) != ProducerIdle)
        {
            throw new InvalidOperationException("The frame-send operation already has an active lease.");
        }

        var generation = NextLeaseGeneration();
        _lifecycle.Initialize();
        Volatile.Write(ref _producerState, ProducerActive);
        var result = new ValueTask(this, _source.Version);
        RegisterPendingOperationCore(pendingOperation, generation);
        return result;
    }

    /// <summary>Registers the next suspension in the current send lease.</summary>
    protected void RegisterPendingOperation(ValueTask pendingOperation)
    {
        if (Volatile.Read(ref _producerState) != ProducerActive)
        {
            throw new InvalidOperationException("The frame-send producer has already completed.");
        }

        RegisterPendingOperationCore(
            pendingOperation,
            Volatile.Read(ref _leaseGeneration));
    }

    /// <summary>Publishes successful producer completion after transport cleanup.</summary>
    protected void PublishResult() => Publish(error: null);

    /// <summary>Publishes failed producer completion after transport cleanup.</summary>
    protected void PublishException(Exception error) =>
        Publish(error ?? throw new ArgumentNullException(nameof(error)));

    public void GetResult(short token)
    {
        if (!_lifecycle.TryBeginReading())
        {
            throw new InvalidOperationException(
                "The send operation result is already being consumed or was consumed.");
        }

        if (token != _source.Version)
        {
            _lifecycle.RollBackReading();
            throw new InvalidOperationException("The send operation token is no longer valid.");
        }

        ValueTaskSourceStatus status;
        try
        {
            status = _source.GetStatus(token);
        }
        catch
        {
            _lifecycle.RollBackReading();
            throw;
        }

        if (status == ValueTaskSourceStatus.Pending)
        {
            _lifecycle.RollBackReading();
            throw new InvalidOperationException("The send operation has not completed.");
        }

        try
        {
            _source.GetResult(token);
        }
        finally
        {
            if (_lifecycle.FinishReading())
            {
                Recycle();
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    /// <summary>Resumes the typed send driver with the operation whose wait just completed.</summary>
    protected abstract void ResumePendingOperation(ValueTask pendingOperation);

    /// <summary>Lets the typed driver clean up and publish a continuation-registration failure.</summary>
    protected abstract void HandlePendingRegistrationFailure(Exception error);

    /// <summary>
    /// Clears typed references before source completion becomes observable. Implementations must
    /// clear every retained reference before any cleanup step that can throw; transport ownership
    /// cleanup should normally finish before calling a publication method.
    /// </summary>
    protected abstract void ClearExternalState();

    private void RegisterPendingOperationCore(ValueTask pendingOperation, int generation)
    {
        _pendingOperation = pendingOperation;
        try
        {
            _executionContext = ExecutionContext.Capture();
            pendingOperation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(_resume);
        }
        catch (Exception error)
        {
            if (Volatile.Read(ref _producerState) != ProducerActive ||
                generation != Volatile.Read(ref _leaseGeneration))
            {
                throw;
            }

            _executionContext = null;
            _pendingOperation = default;
            HandlePendingRegistrationFailure(error);
        }
    }

    private void Resume()
    {
        var executionContext = _executionContext;
        _executionContext = null;
        if (executionContext is null)
        {
            ResumeCore();
            return;
        }

        ExecutionContext.Run(executionContext, ResumeInContext, this);
    }

    private void ResumeCore()
    {
        var pendingOperation = _pendingOperation;
        _pendingOperation = default;
        ResumePendingOperation(pendingOperation);
    }

    private void Publish(Exception? error)
    {
        if (Interlocked.CompareExchange(
                ref _producerState,
                ProducerPublishing,
                ProducerActive) != ProducerActive)
        {
            throw new InvalidOperationException("The frame-send producer already completed.");
        }

        _executionContext = null;
        _pendingOperation = default;
        try
        {
            ClearExternalState();
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        try
        {
            if (error is null)
            {
                _source.SetResult(true);
            }
            else
            {
                _source.SetException(error);
            }
        }
        finally
        {
            if (_lifecycle.FinishProducer())
            {
                Recycle();
            }
        }
    }

    private void Recycle()
    {
        _source.Reset();
        Volatile.Write(ref _producerState, ProducerIdle);
        Pool.Return((TOperation)this);
    }

    private int NextLeaseGeneration()
    {
        var generation = unchecked(_leaseGeneration + 1);
        if (generation == 0)
        {
            generation = 1;
        }

        Volatile.Write(ref _leaseGeneration, generation);
        return generation;
    }
}
