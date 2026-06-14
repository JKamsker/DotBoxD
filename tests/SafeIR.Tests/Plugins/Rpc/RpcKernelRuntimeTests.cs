using SafeIR;
using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Runtime proof of the kernel RPC service path (Followup #2): a hand-built batch kernel loops over a
/// <c>List&lt;I32&gt;</c> input server-side, calls a host binding per element, accumulates a
/// <c>List&lt;Record&gt;</c> (a list of objects), and returns it in one <see cref="InstalledKernel.InvokeRpcAsync"/>
/// roundtrip — the result is returned, not discarded. Also proves the package (including the manifest's
/// rpcEntrypoint) survives a JSON export/import round-trip and that capability gating still applies.
/// </summary>
public sealed class RpcKernelRuntimeTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly SandboxType RecordType = SandboxType.Record([SandboxType.I32, SandboxType.Bool]);
    private const string KillBindingId = "host.world.kill";
    private const string KillCapability = "game.world.monster.write.kill";

    [Fact]
    public async Task A_batch_kernel_loops_server_side_and_returns_a_list_of_records()
    {
        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallRpcAsync(MonsterKillerPackage());

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3), SandboxValue.FromInt32(4)],
            SandboxType.I32);

        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(4, list.Values.Count);   // one record per monster id, built in one roundtrip
        // Kill succeeds for even ids; each result record is { MonsterId, Success }.
        AssertKill(list.Values[0], 1, false);
        AssertKill(list.Values[1], 2, true);
        AssertKill(list.Values[2], 3, false);
        AssertKill(list.Values[3], 4, true);
    }

    [Fact]
    public async Task A_batch_kernel_round_trips_through_json_and_runs()
    {
        var json = PluginPackageJsonSerializer.Export(MonsterKillerPackage(), indented: true);
        var imported = PluginPackageJsonSerializer.Import(json);
        Assert.Equal("KillMonsters", imported.Manifest.RpcEntrypoint);

        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallRpcAsync(imported);

        var result = await kernel.InvokeRpcAsync([SandboxValue.FromList([SandboxValue.FromInt32(2)], SandboxType.I32)]);

        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 2, true);
    }

    [Fact]
    public async Task A_batch_kernel_is_denied_when_its_capability_is_not_granted()
    {
        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: NoKillPolicy());

        await Assert.ThrowsAnyAsync<Exception>(async () => await server.InstallRpcAsync(MonsterKillerPackage()).AsTask());
    }

    [Fact]
    public async Task Invoking_with_the_wrong_argument_count_throws()
    {
        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallRpcAsync(MonsterKillerPackage());

        await Assert.ThrowsAsync<SandboxRuntimeException>(async () => await kernel.InvokeRpcAsync([]).AsTask());
    }

    private static void AssertKill(SandboxValue value, int expectedId, bool expectedSuccess)
    {
        var record = Assert.IsType<RecordValue>(value);
        Assert.Equal([SandboxValue.FromInt32(expectedId), SandboxValue.FromBool(expectedSuccess)], record.Fields);
    }

    // Builds the verified IR for:
    //   List<Record<I32,Bool>> KillMonsters(List<I32> ids) {
    //     var results = List.empty<Record<I32,Bool>>();
    //     for (var i = 0; i < ids.Count; i++)
    //       results = results.Add(new Record(ids[i], host.world.kill(ids[i])));
    //     return results;
    //   }
    private static PluginPackage MonsterKillerPackage()
    {
        Expression Var(string name) => new VariableExpression(name, Span);
        var getItem = new CallExpression("list.get", [Var("ids"), Var("i")], null, Span);
        var kill = new CallExpression(KillBindingId, [getItem], null, Span);
        var newRecord = new CallExpression("record.new", [getItem, kill], RecordType, Span);
        var loopBody = new Statement[]
        {
            new AssignmentStatement("results", new CallExpression("list.add", [Var("results"), newRecord], null, Span), Span)
        };
        var body = new Statement[]
        {
            new AssignmentStatement("results", new CallExpression("list.empty", [], RecordType, Span), Span),
            new ForRangeStatement(
                "i",
                new LiteralExpression(SandboxValue.FromInt32(0), Span),
                new CallExpression("list.count", [Var("ids")], null, Span),
                loopBody,
                Span),
            new ReturnStatement(Var("results"), Span)
        };

        var function = new SandboxFunction(
            "KillMonsters",
            IsEntrypoint: true,
            [new Parameter("ids", SandboxType.List(SandboxType.I32))],
            SandboxType.List(RecordType),
            body);
        var module = new SandboxModule(
            "monster-killer",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "monster-killer", ["kernel"] = "MonsterKillerKernel" });
        var manifest = new PluginManifest(
            "monster-killer",
            "IMonsterKillerService",
            ExecutionMode.Auto,
            ["Cpu", "Alloc", "HostStateWrite"],
            [],
            [])
        {
            RequiredCapabilities = [KillCapability],
            RpcEntrypoint = "KillMonsters"
        };
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("KillMonsters", "KillMonsters"));
    }

    private static void AddKillBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            KillBindingId,
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            KillCapability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var monsterId = ((I32Value)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: KillBindingId,
                    CapabilityId: KillCapability,
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: $"monster:{monsterId}",
                    Fields: context.BindingAuditFields("world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(monsterId % 2 == 0));
            },
            CompiledBinding.RuntimeStub("SafeIR.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy KillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite)
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static SandboxPolicy NoKillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .Build();
}
