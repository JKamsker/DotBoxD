using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Guards finding #20: object-initializer members must be evaluated in SOURCE (lexical) order, not in
/// field-declaration order. The members below are written B-then-A while the fields are declared A-then-B; a
/// shared host counter (<c>Next()</c> returns 1, 2, ...) makes the actual evaluation order observable. Source
/// order means B sees the first <c>Next()</c> (1) and A the second (2), so <c>A*10 + B == 21</c>. Before the
/// fix the members were placed at their declaration index without source-order hoisting, so A was evaluated
/// first (1) and B second (2), yielding 12.
/// </summary>
public sealed class ServerExtensionObjectInitializerOrderTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IOps
        {
            [HostBinding("host.ops.next", "ops.next", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Next();
        }

        public sealed class Pair
        {
            public int A { get; init; }
            public int B { get; init; }
        }

        [ServerExtension("init-order")]
        public sealed partial class InitOrderKernel
        {
            public int Build(HookContext ctx)
            {
                var pair = new Pair { B = ctx.Host<IOps>().Next(), A = ctx.Host<IOps>().Next() };
                return pair.A * 10 + pair.B;
            }
        }
        """;

    [Fact]
    public async Task Object_initializer_members_evaluate_in_source_order_not_declaration_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(Source, "Sample.InitOrderPluginPackage");

        var next = 0;
        using var server = PluginServer.Create(
            configureHost: builder => AddNextBinding(builder, () => ++next),
            defaultPolicy: NextPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(21, Assert.IsType<I32Value>(result).Value);
    }

    private static void AddNextBinding(SandboxHostBuilder builder, Func<int> next)
        => builder.AddBinding(new BindingDescriptor(
            "host.ops.next",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "ops.next",
            BindingCostModel.Fixed(1),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, _, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.ops.next",
                    CapabilityId: "ops.next",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: "ops",
                    Fields: context.BindingAuditFields("ops", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(next()));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy NextPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("ops.next", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
