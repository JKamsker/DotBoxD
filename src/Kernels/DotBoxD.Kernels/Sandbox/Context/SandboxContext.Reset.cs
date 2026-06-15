namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    internal void ResetForCompiledNoAuditReuse()
    {
        _deterministicRandom = null;
        _returnCredits = null;
        _callDepth = 0;
    }
}
