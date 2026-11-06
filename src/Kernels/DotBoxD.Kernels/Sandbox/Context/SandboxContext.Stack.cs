using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    /// <summary>
    /// Fails the run with a quota error before deep (host-raisable) call depth or expression nesting
    /// can overflow the managed stack, which would terminate the host process. Every interpreted and
    /// compiled call goes through <see cref="EnterCall"/>, and the interpreter's generic expression
    /// evaluator checks it per node.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void EnsureSufficientExecutionStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "execution stack depth exceeded"));
        }
    }
}
