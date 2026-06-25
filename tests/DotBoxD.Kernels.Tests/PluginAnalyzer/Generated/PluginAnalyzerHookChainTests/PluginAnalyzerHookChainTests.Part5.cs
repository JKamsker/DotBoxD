using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                {
                    var staged = subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_server_remote_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [DotBoxDService]
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
                    {
                        var staged = this.Server.Hooks.On<DamageEvent>()
                            .Where(e => e.TargetId == "monster-1");
                        staged.Use<DamageKernel>();
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
