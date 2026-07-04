using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginKernelRevocation
{
    internal static SandboxError Error()
        => new(
            SandboxErrorCode.PolicyDenied,
            "plugin kernel capability was revoked");

    public static void ThrowIfRevoked(bool revoked)
    {
        if (!revoked)
        {
            return;
        }

        throw new SandboxRuntimeException(Error());
    }
}
