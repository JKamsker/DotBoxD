using DotBoxD.Services.Server;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedProxySynchronousCancellationTests
{
    [Fact]
    public async Task Generated_task_proxy_returns_canceled_task_when_invoker_throws_synchronous_cancellation()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = (Task<int>)InvokeProxy(proxy, "GetTaskAsync", cts.Token);

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Generated_value_task_proxy_returns_canceled_task_when_invoker_throws_synchronous_cancellation()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = ((ValueTask<int>)InvokeProxy(proxy, "GetValueTaskAsync", cts.Token)).AsTask();

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Generated_void_task_proxy_returns_canceled_task_when_invoker_throws_synchronous_cancellation()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = (Task)InvokeProxy(proxy, "PingTaskAsync", cts.Token);

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Generated_void_value_task_proxy_returns_canceled_task_when_invoker_throws_synchronous_cancellation()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = ((ValueTask)InvokeProxy(proxy, "PingValueTaskAsync", cts.Token)).AsTask();

        Assert.True(invoker.LastToken.IsCancellationRequested);
        Assert.False(task.IsFaulted, FaultedStatusMessage(task));
        Assert.True(task.IsCanceled, FaultedStatusMessage(task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static object CreateProxy(out SynchronousCancellationInvoker invoker)
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Surprise.ProxyCancellation
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
        var interfaceType = assembly.GetType("Surprise.ProxyCancellation.ICancellationProbe")
            ?? throw new InvalidOperationException("generated test interface was not emitted");
        var proxyType = assembly.GetTypes().Single(type =>
            type.IsClass && type.Name.EndsWith("Proxy", StringComparison.Ordinal) && interfaceType.IsAssignableFrom(type));

        invoker = new SynchronousCancellationInvoker();
        return Activator.CreateInstance(proxyType, invoker)
            ?? throw new InvalidOperationException("generated proxy could not be constructed");
    }

    private static object InvokeProxy(object proxy, string methodName, CancellationToken ct)
    {
        var method = proxy.GetType().GetMethod(methodName, [typeof(CancellationToken)])
            ?? throw new InvalidOperationException($"generated proxy did not contain {methodName}");
        return method.Invoke(proxy, [ct])
            ?? throw new InvalidOperationException($"generated proxy returned null for {methodName}");
    }

    private static string FaultedStatusMessage(Task task)
    {
        var exception = task.Exception?.GetBaseException();
        return exception is null
            ? $"task status was {task.Status}"
            : $"task status was {task.Status} with {exception.GetType().Name}: {exception.Message}";
    }

    private sealed class SynchronousCancellationInvoker : IRpcInvoker
    {
        public CancellationToken LastToken { get; private set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TQ, TR>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.FromResult(default(TR)!);
        }

        public Task<TR> InvokeAsync<TR>(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.FromResult(default(TR)!);
        }

        public Task InvokeAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.CompletedTask;
        }

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return Task.CompletedTask;
        }

        public ValueTask<TR> InvokeValueAsync<TQ, TR>(
            string service,
            string method,
            TQ request,
            CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return ValueTask.FromResult(default(TR)!);
        }

        public ValueTask<TR> InvokeValueAsync<TR>(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return ValueTask.FromResult(default(TR)!);
        }

        public ValueTask InvokeValueAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvokeValueAsync(string service, string method, CancellationToken ct = default)
        {
            ThrowIfCanceled(ct);
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

        private void ThrowIfCanceled(CancellationToken ct)
        {
            LastToken = ct;
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            throw new InvalidOperationException("The generated proxy did not pass a canceled caller token.");
        }
    }
}
