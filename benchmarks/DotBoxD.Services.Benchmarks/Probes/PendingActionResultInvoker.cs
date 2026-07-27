using System.Threading.Tasks.Sources;
using DotBoxD.Services.Server;
using Shared;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class PendingActionResultInvoker : IRpcInvoker, IValueTaskSource<ActionResult>
{
    private readonly string _expectedService;
    private readonly string _expectedMethod;
    private readonly MoveRequest _expectedRequest;
    private ManualResetValueTaskSourceCore<ActionResult> _source;
    private bool _pending;

    public PendingActionResultInvoker(
        string expectedService,
        string expectedMethod,
        MoveRequest expectedRequest)
    {
        _expectedService = expectedService;
        _expectedMethod = expectedMethod;
        _expectedRequest = expectedRequest;
    }

    public long CallCount { get; private set; }

    public long CompletionCount { get; private set; }

    public long ResultReadCount { get; private set; }

    public long NonDefaultTokenCount { get; private set; }

    public ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        ValidateInvocation<TRequest, TResponse>(service, method, request, ct);
        if (_pending)
        {
            throw new InvalidOperationException("The previous reusable source operation was not consumed.");
        }

        _source.Reset();
        _pending = true;
        CallCount++;
        return new ValueTask<TResponse>(
            (IValueTaskSource<TResponse>)(object)this,
            _source.Version);
    }

    public void Complete(ActionResult result)
    {
        if (!_pending)
        {
            throw new InvalidOperationException("No reusable source operation is pending.");
        }

        CompletionCount++;
        _source.SetResult(result);
    }

    public ActionResult GetResult(short token)
    {
        try
        {
            return _source.GetResult(token);
        }
        finally
        {
            _pending = false;
            ResultReadCount++;
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) => throw Unsupported();

    public Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) => throw Unsupported();

    public Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) => throw Unsupported();

    public Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default) => throw Unsupported();

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) => throw Unsupported();

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) => throw Unsupported();

    public Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) => throw Unsupported();

    public Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) => throw Unsupported();

    private void ValidateInvocation<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct)
    {
        if (typeof(TRequest) != typeof(MoveRequest) ||
            typeof(TResponse) != typeof(ActionResult) ||
            request is not MoveRequest move ||
            !string.Equals(service, _expectedService, StringComparison.Ordinal) ||
            !string.Equals(method, _expectedMethod, StringComparison.Ordinal) ||
            !string.Equals(move.PlayerId, _expectedRequest.PlayerId, StringComparison.Ordinal) ||
            move.X != _expectedRequest.X ||
            move.Y != _expectedRequest.Y ||
            move.Z != _expectedRequest.Z)
        {
            throw new InvalidOperationException("The benchmark invoked an unexpected generated RPC shape.");
        }

        if (ct != default)
        {
            NonDefaultTokenCount++;
        }
    }

    private static NotSupportedException Unsupported() =>
        new("Only the ValueTask request/response invocation is supported by this probe.");
}
