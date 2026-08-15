using System.Collections.Concurrent;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemotePipelineAsyncInstallSurpriseTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData("hooks")]
    [InlineData("subscriptions")]
    public async Task Generated_local_terminal_completes_with_an_asynchronous_install_callback(string registryKind)
    {
        var installCalls = 0;
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new QueuedSynchronizationContext();
        var worker = new Thread(() =>
        {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                RegisterLocalTerminal(registryKind, async _ =>
                {
                    Interlocked.Increment(ref installCalls);
                    installStarted.TrySetResult();
                    await Task.Yield();
                    return "subscription";
                });
                terminalCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                terminalCompletion.TrySetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        })
        {
            IsBackground = true
        };

        worker.Start();
        await installStarted.Task.WaitAsync(CompletionTimeout);

        try
        {
            var completed = await Task.WhenAny(terminalCompletion.Task, Task.Delay(CompletionTimeout));

            Assert.Same(terminalCompletion.Task, completed);
            await terminalCompletion.Task;
            Assert.Equal(1, Volatile.Read(ref installCalls));
        }
        finally
        {
            context.RunPostedCallbacks();
            await terminalCompletion.Task.WaitAsync(CompletionTimeout);
        }
    }

    private static void RegisterLocalTerminal(string registryKind, Func<PluginPackage, ValueTask<string>> install)
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        var package = PackageFor<RemoteEvent>();

        if (registryKind == "hooks")
        {
            new RemoteHookRegistry(install, localHandlers)
                .On<RemoteEvent>()
                .UseGeneratedLocalChain(package, static (_, _) => ValueTask.CompletedTask);
            return;
        }

        new RemoteSubscriptionRegistry(install, localHandlers)
            .On<RemoteEvent>()
            .UseGeneratedLocalChain(package, static (_, _) => ValueTask.CompletedTask);
    }

    private static PluginPackage PackageFor<TEvent>()
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    new HookSubscriptionManifest(typeof(TEvent).FullName!, "FireDamageKernel")
                    {
                        LocalTerminal = true,
                        ProjectedType = typeof(TEvent).FullName
                    }
                ]
            }
        };
    }

    private sealed record RemoteEvent(string Id);

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback callback, object? state)
            => _callbacks.Enqueue((callback, state));

        public void RunPostedCallbacks()
        {
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }
}
