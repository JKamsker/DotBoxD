using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Http;

internal static class SafeHttpTimeoutClassifier
{
    internal static bool IsRequestTimeout(
        SandboxContext context,
        CancellationToken cancellationToken,
        bool requestTimeoutExpired)
    {
        if (requestTimeoutExpired)
        {
            return true;
        }

        if (context.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (WallTimeExpiredOrNearlyExpired(context))
        {
            return true;
        }

        return !cancellationToken.IsCancellationRequested;
    }

    internal static bool WallTimeExpiredOrNearlyExpired(SandboxContext context)
    {
        try
        {
            return context.Budget.RemainingWallTime() <= TimeSpan.FromMilliseconds(20);
        }
        catch (SandboxRuntimeException ex) when (ex.Error.Code == SandboxErrorCode.Timeout)
        {
            return true;
        }
    }
}
