namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterProbeDispatcher
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Contains("--probe-compiled", StringComparer.OrdinalIgnoreCase))
        {
            await CompiledSpeedProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-bindings", StringComparer.OrdinalIgnoreCase))
        {
            await BindingCrossingProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-matrix", StringComparer.OrdinalIgnoreCase))
        {
            await PerformanceMatrixProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-branched-f64-loop", StringComparer.OrdinalIgnoreCase))
        {
            await BranchedF64LoopProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-frame-layout", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterFrameLayoutProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-plan-setup", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterPlanSetupProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-numeric-conversion", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterNumericConversionProbe.RunAsync();
            return true;
        }

        return false;
    }
}
