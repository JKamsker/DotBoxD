using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainDiagnosticTests
{
    [Fact]
    public void Unlowered_Run_reports_DBXK114_as_error()
    {
        var result = RunGenerator("""
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Run((e, ctx) =>
                        {
                            ctx.Messages.Send(e.TargetId, "damage");
                            return ValueTask.CompletedTask;
                        });
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK114"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Unlowered_RegisterLocal_reports_DBXK113_as_error()
    {
        // RegisterLocal is an explicit local escape hatch, but a call that looks like a lowerable
        // generated chain must still fail closed when the generator cannot prove its shape.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .RegisterLocal((ctx, hookContext) => new DamageResult { Success = true, Damage = ctx.Damage }, priority: 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void RegisterLocal_with_an_unsupported_event_member_reports_the_reason()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("damage", typeof(DamageResult))]
            public sealed record DamageContext(short Unsupported);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageContext>()
                        .RegisterLocal((ctx, _) => new DamageResult(true, null, ctx.Unsupported));
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("not wire-eligible", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Use_reports_DBXK100_error_instead_of_installing_unfiltered_package()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Use_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                    => subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_server_remote_staged_Use_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [RpcService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace Sample.Plugin
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : Sample.Game.IGameWorld;

                public sealed partial class RemotePluginContext;
                public sealed record DamageEvent(string TargetId);
                public sealed class DamageKernel;

                public sealed class Usage
                {
                    public RemotePluginServer Server { get; init; } = null!;

                    public void Configure()
                        => this.Server.Hooks.On<DamageEvent>()
                            .Where(e => e.TargetId == "monster-1")
                            .Use<DamageKernel>();
                }
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Where/Select", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("Use", StringComparison.Ordinal));
    }

}
