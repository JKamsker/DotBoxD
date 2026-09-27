using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

internal static class CompiledAwaitPumpFixture
{
    public static SandboxExecutionResult Result() => new()
    {
        Succeeded = true,
        Value = SandboxValue.Unit,
        ResourceUsage = new(0, 1_000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        AuditEvents = [],
        ModuleHash = "module",
        PlanHash = "plan",
        PolicyHash = "policy"
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (SynchronizationContext Context, WeakReference[] References) CreateDisposedQueue(int count)
    {
        SynchronizationContext? context = null;
        var references = new WeakReference[count];
        var result = Result();
        _ = CompiledAsyncWorker.RunInline(() =>
        {
            context = SynchronizationContext.Current!;
            for (var index = 0; index < count; index++)
            {
                var state = new object();
                references[index] = new WeakReference(state);
                context.Post(static _ => { }, state);
            }
            return result;
        });
        return (context!, references);
    }

    public static async Task AssertCollectedAsync(WeakReference[] references)
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(20);
        } while (Environment.TickCount64 < deadline);

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }
}
