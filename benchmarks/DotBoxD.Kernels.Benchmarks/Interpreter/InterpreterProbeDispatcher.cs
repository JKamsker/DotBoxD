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

        if (args.Contains("--probe-interpreter-i64-plan-setup", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterI64PlanSetupProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-while-plan-setup", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterWhilePlanSetupProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-branched-plan-setup", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterBranchedPlanSetupProbe.RunAsync();
            return true;
        }

        if (args.Contains("--probe-interpreter-local-call-arguments", StringComparer.OrdinalIgnoreCase))
        {
            await InterpreterLocalCallArgumentProbe.RunAsync();
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
