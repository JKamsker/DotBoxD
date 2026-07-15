using DotBoxD.Kernels.Compiler;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Execution;

internal sealed class DisposingExecutionModeSelector : IExecutionModeSelector
{
    public SandboxHost Host { get; set; } = null!;
    public int Calls { get; private set; }

    public ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus)
    {
        Calls++;
        Host.Dispose();
        return ExecutionModeDecision.Interpreted;
    }
}
