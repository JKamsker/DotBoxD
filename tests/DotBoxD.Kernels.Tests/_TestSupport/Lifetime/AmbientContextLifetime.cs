using System.Runtime.CompilerServices;

namespace DotBoxD.Kernels.Tests._TestSupport;

internal static class AmbientContextLifetime
{
    internal static AsyncLocal<object?> Current { get; } = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference Capture(Action action)
    {
        var value = new object();
        var reference = new WeakReference(value);
        var previous = Current.Value;
        Current.Value = value;
        try
        {
            action();
            Assert.Same(value, Current.Value);
        }
        finally
        {
            Current.Value = previous;
        }
        return reference;
    }

    internal static void AssertRetained(WeakReference reference)
    {
        Collect();
        Assert.True(reference.IsAlive, "An active waiter must retain its execution context.");
    }

    internal static async Task AssertCollectedAsync(WeakReference reference)
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            Collect();
            if (!reference.IsAlive)
            {
                return;
            }
            await Task.Delay(10);
        } while (Environment.TickCount64 < deadline);
        Assert.False(reference.IsAlive, "A canceled waiter's fault observer must not retain its ambient state.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
