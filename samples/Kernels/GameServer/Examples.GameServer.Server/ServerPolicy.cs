using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Game.Server;

/// <summary>
/// Per-kernel least-privilege policies. Every kernel gets logging, the example-defined
/// <c>host.message.write</c> capability, and deterministic fuel/host-call budgets. A kernel whose
/// server-side package analysis finds a <c>game.world.monster.read.*</c> capability additionally receives
/// the matching wildcard grant; a kernel that does not (the retaliation kernel) is not over-granted. The
/// wildcard covers
/// <c>game.world.monster.read.health</c> but not <c>game.world.combat.threat</c> (<c>GetThreat</c>), so
/// a kernel that reads threat is denied at install. Without the message-write grant, package
/// preparation fails closed too.
/// Server extensions get the same least-privilege treatment; the monster-killer batch kernel receives
/// <c>game.world.monster.write.*</c> only because its verified IR declares the kill binding.
/// </summary>
internal static class ServerPolicy
{
    private const string MonsterReadPrefix = "game.world.monster.read.";
    private const string MonsterWritePrefix = "game.world.monster.write.";

    /// <summary>The base ceiling applied to a kernel with no extra capability needs.</summary>
    public static SandboxPolicy Create() => ForKernel([]);

    /// <summary>
    /// Builds the policy granting exactly what server-side package analysis says the verified IR needs.
    /// </summary>
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000);

        if (requiredCapabilities.Any(capability =>
                capability.StartsWith(MonsterReadPrefix, StringComparison.Ordinal)))
        {
            builder.Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead);
        }

        if (requiredCapabilities.Any(capability =>
                capability.StartsWith(MonsterWritePrefix, StringComparison.Ordinal)))
        {
            builder.Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite);
        }

        return builder.Build();
    }
}
