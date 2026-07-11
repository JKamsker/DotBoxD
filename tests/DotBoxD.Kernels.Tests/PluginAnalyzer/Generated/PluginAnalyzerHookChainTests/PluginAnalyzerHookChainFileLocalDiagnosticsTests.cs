using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainFileLocalDiagnosticsTests
{
    [Fact]
    public void Remote_RunLocal_rejects_file_local_typed_server_contexts_before_generating_sources()
    {
        var (output, result) = RunGeneratorCore("""
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            public sealed record DamageEvent(int Amount);

            file sealed class FileServerContext
            {
                public FileServerContext(HookContext raw) => Raw = raw;

                public HookContext Raw { get; }
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent, FileServerContext>(raw => new FileServerContext(raw))
                        .RunLocal((e, ctx) => { _ = ctx.Raw; });
            }
            """);

        var generatorDiagnostics = result.Diagnostics.ToArray();
        var outputDiagnostics = output.GetDiagnostics().ToArray();
        var hasFocusedDiagnostic = generatorDiagnostics.Any(diagnostic => diagnostic.Id == "DBXK100");
        var rawFileLocalCs0234 = outputDiagnostics
            .Where(diagnostic => diagnostic.Id == "CS0234" &&
                                 diagnostic.GetMessage().Contains("FileServerContext", StringComparison.Ordinal))
            .ToArray();
        var generatedHookChainSources = result.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .Where(source => source.Contains("DotBoxDHookChainInterceptors", StringComparison.Ordinal) ||
                             source.Contains("HookChain_", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hasFocusedDiagnostic && rawFileLocalCs0234.Length == 0 && generatedHookChainSources.Length == 0,
            string.Join(
                Environment.NewLine,
                [
                    "Expected a focused DBXK100 diagnostic before hook-chain interceptor/package output.",
                    $"Has DBXK100: {hasFocusedDiagnostic}",
                    "Generator diagnostics:",
                    .. generatorDiagnostics.Select(diagnostic => diagnostic.ToString()),
                    "Raw file-local CS0234 diagnostics:",
                    .. rawFileLocalCs0234.Select(diagnostic => diagnostic.ToString()),
                    $"Generated hook-chain source count: {generatedHookChainSources.Length}",
                ]));
    }

    [Fact]
    public void Remote_RunLocal_rejects_file_local_projected_payload_types_before_generating_sources()
    {
        var (output, result) = RunGeneratorCore("""
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            public sealed record DamageEvent(int Amount);
            file sealed record FilePayload(int Amount);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Select((e, ctx) => new FilePayload(e.Amount))
                        .RunLocal((payload, ctx) => { _ = payload.Amount; });
            }
            """);

        var generatorDiagnostics = result.Diagnostics.ToArray();
        var outputDiagnostics = output.GetDiagnostics().ToArray();
        var hasFocusedDiagnostic = generatorDiagnostics.Any(diagnostic => diagnostic.Id == "DBXK100");
        var rawFileLocalCs0234 = outputDiagnostics
            .Where(diagnostic => diagnostic.Id == "CS0234" &&
                                 diagnostic.GetMessage().Contains("FilePayload", StringComparison.Ordinal))
            .ToArray();
        var generatedHookChainSources = result.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .Where(source => source.Contains("DotBoxDHookChainInterceptors", StringComparison.Ordinal) ||
                             source.Contains("HookChain_", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hasFocusedDiagnostic && rawFileLocalCs0234.Length == 0 && generatedHookChainSources.Length == 0,
            string.Join(
                Environment.NewLine,
                [
                    "Expected a focused DBXK100 diagnostic before hook-chain interceptor/package output.",
                    $"Has DBXK100: {hasFocusedDiagnostic}",
                    "Generator diagnostics:",
                    .. generatorDiagnostics.Select(diagnostic => diagnostic.ToString()),
                    "Raw file-local CS0234 diagnostics:",
                    .. rawFileLocalCs0234.Select(diagnostic => diagnostic.ToString()),
                    $"Generated hook-chain source count: {generatedHookChainSources.Length}",
                ]));
    }

    [Fact]
    public void Remote_subscription_RunLocal_rejects_file_local_typed_contexts_before_generating_sources()
    {
        var (output, result) = RunGeneratorCore("""
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Plugins.Runtime.Hooks;

            namespace Regression.Game;

            public sealed record DamageEvent(int Amount);
            file sealed record FileSubscriptionContext(HookContext Raw);

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                    => subscriptions.On<DamageEvent, FileSubscriptionContext>(
                            ctx => new FileSubscriptionContext(ctx))
                        .RunLocal((e, ctx) => { _ = ctx.Raw; });
            }
            """);

        var generatorDiagnostics = result.Diagnostics.ToArray();
        var outputDiagnostics = output.GetDiagnostics().ToArray();
        var hasFocusedDiagnostic = generatorDiagnostics.Any(diagnostic => diagnostic.Id == "DBXK100");
        var rawFileLocalCompilerDiagnostics = outputDiagnostics
            .Where(diagnostic => (diagnostic.Id == "CS0234" || diagnostic.Id == "CS0246") &&
                                 diagnostic.GetMessage().Contains("FileSubscriptionContext", StringComparison.Ordinal))
            .ToArray();
        var generatedHookChainSources = result.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .Where(source => source.Contains("DotBoxDHookChainInterceptors", StringComparison.Ordinal) ||
                             source.Contains("HookChain_", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hasFocusedDiagnostic && rawFileLocalCompilerDiagnostics.Length == 0 && generatedHookChainSources.Length == 0,
            string.Join(
                Environment.NewLine,
                [
                    "Expected a focused DBXK100 diagnostic before subscription-chain interceptor/package output.",
                    $"Has DBXK100: {hasFocusedDiagnostic}",
                    "Generator diagnostics:",
                    .. generatorDiagnostics.Select(diagnostic => diagnostic.ToString()),
                    "Raw file-local compiler diagnostics:",
                    .. rawFileLocalCompilerDiagnostics.Select(diagnostic => diagnostic.ToString()),
                    $"Generated hook-chain source count: {generatedHookChainSources.Length}",
                ]));
    }
}
