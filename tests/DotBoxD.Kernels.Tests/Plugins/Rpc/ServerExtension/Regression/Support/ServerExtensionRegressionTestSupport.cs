using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

internal static class ServerExtensionRegressionTestSupport
{
    internal static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
