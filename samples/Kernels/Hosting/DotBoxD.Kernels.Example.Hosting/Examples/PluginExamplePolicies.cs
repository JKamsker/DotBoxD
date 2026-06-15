using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Example.Hosting.Examples;

internal static class PluginExamplePolicies
{
    public static SandboxPolicy MessageWrite()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
}
