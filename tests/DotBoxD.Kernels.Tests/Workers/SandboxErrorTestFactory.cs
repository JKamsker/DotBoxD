using System.Reflection;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Workers;

internal static class SandboxErrorTestFactory
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static SandboxError WithCode(SandboxErrorCode code, string safeMessage)
    {
        var error = new SandboxError(SandboxErrorCode.HostFailure, safeMessage);
        var codeField = typeof(SandboxError).GetField("_code", PrivateInstance)
            ?? throw new MissingFieldException(typeof(SandboxError).FullName, "_code");

        codeField.SetValue(error, code);
        return error;
    }
}
