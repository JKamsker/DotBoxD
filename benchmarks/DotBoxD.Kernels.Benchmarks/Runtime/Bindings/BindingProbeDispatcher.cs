namespace DotBoxD.Kernels.Benchmarks.Runtime.Bindings;

internal static class BindingProbeDispatcher
{
    public static async ValueTask<bool> TryRunAsync(string[] args)
    {
        if (args.Contains(
                "--probe-compiled-binding-two-argument-fallback",
                StringComparer.OrdinalIgnoreCase))
        {
            CompiledBindingTwoArgumentFallbackProbe.Run();
            return true;
        }

        if (args.Contains(
                "--probe-compiled-binding-arity-three",
                StringComparer.OrdinalIgnoreCase))
        {
            await CompiledBindingArityThreeProbe.RunAsync().ConfigureAwait(false);
            return true;
        }

        return false;
    }
}
