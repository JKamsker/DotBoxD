using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// The result-hook facade on the host hook surface: the Register/RegisterLocal terminals throw until the
/// analyzer lowers them (so plugin logic never runs unsandboxed by accident), and FireAsync returns null when
/// nothing is registered.
/// </summary>
public sealed class HookPipelineResultHooksTests
{
    private sealed record DamageCtx(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    [Fact]
    public void Register_throws_until_lowered()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());

        Assert.Throws<SandboxValidationException>(() => pipeline.Register<DamageResult>(c => default, priority: 0));
    }

    [Fact]
    public void RegisterLocal_throws_until_lowered()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());

        Assert.Throws<SandboxValidationException>(
            () => pipeline.RegisterLocal<DamageResult>((c, ctx) => default, priority: 0));
    }

    [Fact]
    public async Task FireAsync_returns_null_when_no_hook_point_exists()
    {
        using var server = PluginServer.Create();

        var result = await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10));

        Assert.Null(result);
    }

    [Fact]
    public async Task FireAsync_returns_null_when_a_hook_point_has_no_result_handlers()
    {
        using var server = PluginServer.Create();
        server.Hooks.On<DamageCtx>(new StubAdapter());

        var result = await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10));

        Assert.Null(result);
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
