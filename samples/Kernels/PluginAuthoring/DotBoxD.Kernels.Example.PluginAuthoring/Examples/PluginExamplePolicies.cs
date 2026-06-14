namespace DotBoxD.Kernels.Example.PluginAuthoring;

using DotBoxD.Kernels;
using DotBoxD.Plugins;

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
