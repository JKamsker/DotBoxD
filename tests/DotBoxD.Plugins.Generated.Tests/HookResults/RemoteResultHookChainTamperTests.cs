using DotBoxD.Abstractions;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class RemoteResultHookChainTests
{
    [Fact]
    public void Remote_RegisterLocal_tamper_rejects_direct_result_local_terminal()
    {
        var package = WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with { ResultLocalTerminal = false });
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage)));

        Assert.False(installed);
        Assert.Contains("resultLocalTerminal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_tamper_rejects_direct_result_type()
    {
        var package = WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with
            {
                ResultType = "global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteOtherDamageResult"
            });
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage)));

        Assert.False(installed);
        Assert.Contains(nameof(RemoteOtherDamageResult), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_tamper_rejects_json_result_local_terminal()
    {
        var package = JsonRoundTrip(WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with { ResultLocalTerminal = false }));
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage)));

        Assert.False(installed);
        Assert.Contains("resultLocalTerminal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_tamper_rejects_json_result_type()
    {
        var package = JsonRoundTrip(WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with
            {
                ResultType = "global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteOtherDamageResult"
            }));
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage)));

        Assert.False(installed);
        Assert.Contains(nameof(RemoteOtherDamageResult), exception.Message, StringComparison.Ordinal);
    }

    private static PluginPackage JsonRoundTrip(PluginPackage package)
        => PluginPackageJsonSerializer.Import(PluginPackageJsonSerializer.Export(package));
}
