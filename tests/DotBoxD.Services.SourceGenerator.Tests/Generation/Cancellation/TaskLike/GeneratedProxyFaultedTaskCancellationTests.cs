using DotBoxD.Services.Server;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedProxyFaultedTaskCancellationTests
{
    [Fact]
    public async Task Task_of_T_proxy_prefers_caller_cancellation_over_a_faulted_invoker_task()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancellationSource = cts;

        var task = (Task<int>)InvokeProxy(proxy, "GetTaskAsync", cts.Token);

        await AssertCanceledWithCallerTokenAsync(task, cts.Token);
    }

    [Fact]
    public async Task ValueTask_of_T_proxy_prefers_caller_cancellation_over_a_faulted_invoker_task()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancellationSource = cts;

        var task = ((ValueTask<int>)InvokeProxy(proxy, "GetValueTaskAsync", cts.Token)).AsTask();

        await AssertCanceledWithCallerTokenAsync(task, cts.Token);
    }

    [Fact]
    public async Task Task_of_T_proxy_prefers_caller_cancellation_over_a_synchronous_invoker_fault()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();
        invoker.CancellationSource = cts;
        invoker.ThrowSynchronously = true;

        var task = (Task<int>)InvokeProxy(proxy, "GetTaskAsync", cts.Token);

        await AssertCanceledWithCallerTokenAsync(task, cts.Token);
    }

    [Fact]
    public async Task Task_like_proxies_preserve_an_ordinary_invoker_fault_when_the_caller_token_is_live()
    {
        var proxy = CreateProxy(out var invoker);
        using var cts = new CancellationTokenSource();

        var task = (Task<int>)InvokeProxy(proxy, "GetTaskAsync", cts.Token);
        var valueTask = ((ValueTask<int>)InvokeProxy(proxy, "GetValueTaskAsync", cts.Token)).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => valueTask);
        Assert.False(cts.IsCancellationRequested);
        Assert.Equal(2, invoker.CallCount);
    }

    private static async Task AssertCanceledWithCallerTokenAsync(Task task, CancellationToken callerToken)
    {
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(callerToken, exception.CancellationToken);
        Assert.True(task.IsCanceled, TaskStatusMessage(task));
    }

    private static object CreateProxy(out FaultingTaskCancellationInvoker invoker)
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Surprise.ProxyFaultedTaskCancellation
            {
                [RpcService]
                public interface ICancellationProbe
                {
                    Task<int> GetTaskAsync(CancellationToken ct = default);
                    ValueTask<int> GetValueTaskAsync(CancellationToken ct = default);
                }
            }
            """;

        var assembly = GeneratedRoundTripTestSupport.CompileAndLoad(source);
        var interfaceType = assembly.GetType("Surprise.ProxyFaultedTaskCancellation.ICancellationProbe")
            ?? throw new InvalidOperationException("generated test interface was not emitted");
        var proxyType = assembly.GetTypes().Single(type =>
            type.IsClass && type.Name.EndsWith("Proxy", StringComparison.Ordinal) && interfaceType.IsAssignableFrom(type));

        invoker = new FaultingTaskCancellationInvoker();
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

    private static string TaskStatusMessage(Task task)
    {
        var exception = task.Exception?.GetBaseException();
        return exception is null
            ? $"task status was {task.Status}"
            : $"task status was {task.Status} with {exception.GetType().Name}: {exception.Message}";
    }

    private sealed class FaultingTaskCancellationInvoker : IRpcInvoker
    {
        public CancellationTokenSource? CancellationSource { get; set; }

        public int CallCount { get; private set; }

        public bool ThrowSynchronously { get; set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TQ, TR>(string service, string method, TQ request, CancellationToken ct = default) =>
            InvokeAsync<TR>(service, method, ct);

        public Task<TR> InvokeAsync<TR>(string service, string method, CancellationToken ct = default) =>
            Fail<TR>(ct);

        public Task InvokeAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default) =>
            Fail<object?>(ct);

        public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
            Fail<object?>(ct);

        public ValueTask<TR> InvokeValueAsync<TQ, TR>(
            string service,
            string method,
            TQ request,
            CancellationToken ct = default) =>
            new(Fail<TR>(ct));

        public ValueTask<TR> InvokeValueAsync<TR>(string service, string method, CancellationToken ct = default) =>
            new(Fail<TR>(ct));

        public ValueTask InvokeValueAsync<TQ>(string service, string method, TQ request, CancellationToken ct = default) =>
            new(Fail<object?>(ct));

        public ValueTask InvokeValueAsync(string service, string method, CancellationToken ct = default) =>
            new(Fail<object?>(ct));

        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
            string service,
            string instanceId,
            string method,
            TQ request,
            CancellationToken ct = default) =>
            Fail<TR>(ct);

        public Task<TR> InvokeOnInstanceAsync<TR>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            Fail<TR>(ct);

        public Task InvokeOnInstanceAsync<TQ>(
            string service,
            string instanceId,
            string method,
            TQ request,
            CancellationToken ct = default) =>
            Fail<object?>(ct);

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            Fail<object?>(ct);

        private Task<TR> Fail<TR>(CancellationToken callerToken)
        {
            CallCount++;
            CancellationSource?.Cancel();

            var exception = new InvalidOperationException("invoker fault sentinel");
            if (ThrowSynchronously)
            {
                throw exception;
            }

            return Task.FromException<TR>(exception);
        }
    }
}
