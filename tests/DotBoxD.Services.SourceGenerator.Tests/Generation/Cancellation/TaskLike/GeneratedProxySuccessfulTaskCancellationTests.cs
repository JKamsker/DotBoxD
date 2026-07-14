using DotBoxD.Services.Server;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedProxySuccessfulTaskCancellationTests
{
    [Fact]
    public async Task Task_of_T_proxy_observes_invoker_canceled_caller_token_before_publishing_result()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancelOnSuccessfulResponse = cts;

        var task = (Task<int>)InvokeProxy(proxy, "GetTaskAsync", cts.Token);

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ValueTask_of_T_proxy_observes_invoker_canceled_caller_token_before_publishing_result()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancelOnSuccessfulResponse = cts;

        var task = ((ValueTask<int>)InvokeProxy(proxy, "GetValueTaskAsync", cts.Token)).AsTask();

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Task_proxy_observes_invoker_canceled_caller_token_before_completing()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancelOnSuccessfulResponse = cts;

        var task = (Task)InvokeProxy(proxy, "PingTaskAsync", cts.Token);

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ValueTask_proxy_observes_invoker_canceled_caller_token_before_completing()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancelOnSuccessfulResponse = cts;

        var task = ((ValueTask)InvokeProxy(proxy, "PingValueTaskAsync", cts.Token)).AsTask();

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static object CreateProxy(out SuccessfulTaskCancellationInvoker invoker)
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Surprise.ProxyTaskCancellation
            {
                [RpcService]
                public interface ICancellationProbe
                {
                    Task<int> GetTaskAsync(CancellationToken ct = default);
                    ValueTask<int> GetValueTaskAsync(CancellationToken ct = default);
                    Task PingTaskAsync(CancellationToken ct = default);
                    ValueTask PingValueTaskAsync(CancellationToken ct = default);
                }
            }
            """;

        var assembly = GeneratedRoundTripTestSupport.CompileAndLoad(source);
        var interfaceType = assembly.GetType("Surprise.ProxyTaskCancellation.ICancellationProbe")
            ?? throw new InvalidOperationException("generated test interface was not emitted");
        var proxyType = assembly.GetTypes().Single(type =>
            type.IsClass && type.Name.EndsWith("Proxy", StringComparison.Ordinal) && interfaceType.IsAssignableFrom(type));

        invoker = new SuccessfulTaskCancellationInvoker();
        return Activator.CreateInstance(proxyType, invoker)
            ?? throw new InvalidOperationException("generated proxy could not be constructed");
    }

    private static object InvokeProxy(object proxy, string methodName, CancellationToken ct)
    {
        var method = proxy.GetType().GetMethod(methodName, [typeof(CancellationToken)])
            ?? throw new InvalidOperationException($"generated proxy did not contain {methodName}");

        try
        {
            return method.Invoke(proxy, [ct])
                ?? throw new InvalidOperationException($"generated proxy returned null for {methodName}");
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static string FaultedStatusMessage(Task task)
    {
        var exception = task.Exception?.GetBaseException();
        return exception is null
            ? $"task status was {task.Status}"
            : $"task status was {task.Status} with {exception.GetType().Name}: {exception.Message}";
    }

    private sealed class SuccessfulTaskCancellationInvoker : IRpcInvoker
    {
        public CancellationToken LastToken { get; private set; }

        public CancellationTokenSource? CancelOnSuccessfulResponse { get; set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TQ, TR>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.FromResult(CreateSuccessfulResponse<TR>(ct));
        }

        public Task<TR> InvokeAsync<TR>(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.FromResult(CreateSuccessfulResponse<TR>(ct));
        }

        public Task InvokeAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            CancelBeforeSuccessfulResponse(ct);
            return Task.CompletedTask;
        }

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            CancelBeforeSuccessfulResponse(ct);
            return Task.CompletedTask;
        }

        public ValueTask<TR> InvokeValueAsync<TQ, TR>(
            string service,
            string method,
            TQ request,
            CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return ValueTask.FromResult(CreateSuccessfulResponse<TR>(ct));
        }

        public ValueTask<TR> InvokeValueAsync<TR>(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return ValueTask.FromResult(CreateSuccessfulResponse<TR>(ct));
        }

        public ValueTask InvokeValueAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            CancelBeforeSuccessfulResponse(ct);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvokeValueAsync(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            CancelBeforeSuccessfulResponse(ct);
            return ValueTask.CompletedTask;
        }

        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
            string service,
            string instanceId,
            string method,
            TQ request,
            CancellationToken ct = default) =>
            InvokeAsync<TQ, TR>(service, method, request, ct);

        public Task<TR> InvokeOnInstanceAsync<TR>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            InvokeAsync<TR>(service, method, ct);

        public Task InvokeOnInstanceAsync<TQ>(
            string service,
            string instanceId,
            string method,
            TQ request,
            CancellationToken ct = default) =>
            InvokeAsync(service, method, request, ct);

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            InvokeAsync(service, method, ct);

        private TR CreateSuccessfulResponse<TR>(CancellationToken ct)
        {
            CancelBeforeSuccessfulResponse(ct);
            if (typeof(TR) == typeof(int))
            {
                return (TR)(object)42;
            }

            return default!;
        }

        private void CancelBeforeSuccessfulResponse(CancellationToken ct)
        {
            LastToken = ct;
            if (CancelOnSuccessfulResponse is { } source)
            {
                source.Cancel();
                return;
            }

            throw new InvalidOperationException("The generated proxy did not pass a canceled caller token.");
        }

        private void ThrowIfCanceled(CancellationToken ct)
        {
            LastToken = ct;
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            if (CancelOnSuccessfulResponse is null)
            {
                throw new InvalidOperationException("The generated proxy did not pass a canceled caller token.");
            }
        }
    }
}
