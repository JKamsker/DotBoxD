namespace DotBoxD.Kernels.Benchmarks.Runtime.Bindings;

internal static class BindingProbeDispatcher
{
    public static async ValueTask<bool> TryRunAsync(string[] args)
    {
        if (!args.Contains(
                "--probe-compiled-binding-arity-three",
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        await CompiledBindingArityThreeProbe.RunAsync().ConfigureAwait(false);
        return true;
    }
}
