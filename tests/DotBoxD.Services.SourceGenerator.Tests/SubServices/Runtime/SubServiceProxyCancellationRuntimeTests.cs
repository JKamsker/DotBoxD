using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using FluentAssertions;
using FluentAssertions.Execution;
using static DotBoxD.Services.SourceGenerator.Tests.SubServices.NestedServiceTestCompiler;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class SubServiceProxyCancellationRuntimeTests
{
    private const string Source = """
        using DotBoxD.Services.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Reviewed.SubServiceProxyCancellation
        {
            [RpcService(Name = "sub-cancel")]
            public interface ISub
            {
                Task<int> CountAsync(CancellationToken ct = default);
            }

            [RpcService(Name = "root-cancel")]
            public interface IRoot
            {
                Task<ISub> OpenTaskAsync(CancellationToken ct = default);
                ValueTask<ISub> OpenValueTaskAsync(CancellationToken ct = default);
            }
        }
        """;

    [Theory]
    [InlineData("OpenTaskAsync")]
    [InlineData("OpenValueTaskAsync")]
    public async Task RootProxy_ThrowsCancellationBeforeMaterializingSubServiceAfterInvokerCancels(
        string methodName)
    {
        var (assembly, _) = Compile(Source);
        using var cts = new CancellationTokenSource();
        var invoker = new CancelingHandleInvoker(cts);
        var rootProxy = CreateRootProxy(assembly, invoker);
        object? returned = null;

        var exception = await Record.ExceptionAsync(async () =>
            returned = await InvokeRootOpenAsync(rootProxy, methodName, cts.Token));

        using var _ = new AssertionScope();
        exception.Should().BeOfType<OperationCanceledException>(
            "the generated proxy should recheck the caller token after awaiting the RPC handle");
        returned.Should().BeNull("a generated sub-service proxy should not be materialized after cancellation");
        cts.IsCancellationRequested.Should().BeTrue();
        invoker.RootCalls.Should().Be(1);
        invoker.InstanceCalls.Should().Be(0);
    }

    private static object CreateRootProxy(Assembly assembly, IRpcInvoker invoker)
        => Activator.CreateInstance(
            assembly.GetType("Reviewed.SubServiceProxyCancellation.RootProxy")!,
            invoker)!;

    private static async Task<object?> InvokeRootOpenAsync(
        object rootProxy,
        string methodName,
        CancellationToken ct)
    {
        var open = rootProxy.GetType().GetMethod(methodName, new[] { typeof(CancellationToken) })!;
        object taskLike;
        try
        {
            taskLike = open.Invoke(rootProxy, new object[] { ct })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return await AwaitResultAsync(taskLike);
    }

    private static async Task<object?> AwaitResultAsync(object taskLike)
    {
        if (taskLike is Task task)
        {
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }

        var asTask = (Task)taskLike.GetType().GetMethod("AsTask")!.Invoke(taskLike, Array.Empty<object>())!;
        await asTask;
        return asTask.GetType().GetProperty("Result")!.GetValue(asTask);
    }

    private sealed class CancelingHandleInvoker(CancellationTokenSource cts) : IRpcInvoker
    {
        public int RootCalls { get; private set; }
        public int InstanceCalls { get; private set; }

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default)
        {
            service.Should().Be("root-cancel");
            method.Should().BeOneOf("OpenTaskAsync", "OpenValueTaskAsync");
            ct.Should().Be(cts.Token);
            RootCalls++;
            cts.Cancel();
            return Task.FromResult((TResponse)(object)new ServiceHandle
            {
                ServiceName = "sub-cancel",
                InstanceId = "sub-1",
            });
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
            => Task.FromResult(default(TResponse)!);

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            InstanceCalls++;
            return Task.FromResult(default(TResponse)!);
        }

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
        {
            InstanceCalls++;
            return Task.FromResult(default(TResponse)!);
        }

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default)
        {
            InstanceCalls++;
            return Task.CompletedTask;
        }

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default)
        {
            InstanceCalls++;
            return Task.CompletedTask;
        }
    }
}
