using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HookPipelineResultHookOptionsTests
{
    [Hook("test.damage", typeof(DamageResult))]
    private sealed record DamageCtx(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    [Fact]
    public async Task FireResultAsync_rejects_null_options_when_hook_point_has_no_result_handlers()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await pipeline.FireResultAsync<DamageResult>(new DamageCtx(10), null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidRemoteHandlerTimeouts))]
    public async Task FireResultAsync_rejects_invalid_options_when_hook_point_has_no_result_handlers(
        TimeSpan remoteHandlerTimeout)
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());
        var options = new ResultHookDispatchOptions<DamageResult>
        {
            RemoteHandlerTimeout = remoteHandlerTimeout,
        };

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await pipeline.FireResultAsync<DamageResult>(new DamageCtx(10), options));

        Assert.Equal("RemoteHandlerTimeout", exception.ParamName);
    }

    public static TheoryData<TimeSpan> InvalidRemoteHandlerTimeouts()
        => new()
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(-1),
            TimeSpan.FromMilliseconds((double)int.MaxValue + 1d),
        };

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
