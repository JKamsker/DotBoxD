using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.UI.Authoring;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.HostCalls;

internal static class UiHostCallFixture
{
    public static UiGeneratorFixture.Generation Generate(string body) => UiGeneratorFixture.Generate(Source(body));

    public static async Task AssertMatchesNative(string body, ExecutionMode mode, int expectedCalls = 0)
    {
        var generated = UiGeneratorFixture.Generate(Source(body) + """
            public static partial class Counter
            {
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    var input = b.State(7);
                    var output = b.State(0);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
                }
            }
            """);
        Assert.Empty(generated.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(generated.Output.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var type = UiGeneratorFixture.Emit(generated.Output).GetType("Counter")!;
        var expected = (int)type.GetMethod("Handle")!.Invoke(null, [7])!;
        var package = (UiPackage)type.GetMethod("Package")!.Invoke(null, null)!;
        var (nextBinding, calls) = UiOperandOrderFixture.NextBinding();
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(ReadBinding()).AddBinding(nextBinding).UseCompilerIfAvailable());
        var policy = SandboxPolicyBuilder.Create().Grant("review.scope.read", new { }, SandboxEffect.HostStateRead)
            .Grant("review.next", new { }, SandboxEffect.HostStateWrite).Build();
        await using var session = await new UiHost(sandbox, policy,
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        var actual = (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Integer;
        Assert.Equal(expected, actual);
        Assert.Equal(expectedCalls, calls());
    }

    private static BindingDescriptor ReadBinding() => new("review.scope.read", SemVersion.One, [SandboxType.I32],
        SandboxType.I32, SandboxEffect.Cpu | SandboxEffect.HostStateRead, "review.scope.read", BindingCostModel.Fixed(1),
        AuditLevel.PerResource, BindingSafety.ReadOnlyExternal,
        (context, arguments, _) =>
        {
            var started = DateTimeOffset.UtcNow;
            context.Audit.Write(new SandboxAuditEvent(context.RunId, "BindingCall", started, true,
                BindingId: "review.scope.read", CapabilityId: "review.scope.read", Effect: SandboxEffect.HostStateRead,
                ResourceId: "key", Fields: context.BindingAuditFields("ui-host", started)));
            return ValueTask.FromResult(arguments[0]);
        }, CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
        GrantValidator: static (_, _) => { });

    private static string Source(string body) => $$"""
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public sealed record Target(int Value)
        {
            [HostBinding("review.target.read", "review.scope.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead,
                IncludeReceiver = true)]
            public int Read() => Value;
        }
        [HostBindingObject("review.object", "review.scope.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        public sealed record ObjectTarget(int Value)
        {
            public int Read() => Value;
        }
        public sealed record Unforwarded(int Value)
        {
            [HostBinding("review.target.read", "review.scope.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            public int Read() => 42;
        }
        [RpcService]
        public interface IScope
        {
            [HostBinding("review.scope.read", "review.scope.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Read();
        }
        public sealed class Scope(int key) : IScope { public int Read() => key; }
        public static partial class Counter
        {
            private static int next;
            [HostBinding("review.next", "review.next", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static int Next() => ++next;
            [HostBinding("review.scope.read", "review.scope.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            public static int ReadKey(int key) => key;
            public static IScope Get(int key) => new Scope(key);
            public static IScope GetAdjusted(int key) => new Scope(key + 1);
            public static Unforwarded Make(int key) => throw new System.InvalidOperationException("receiver failed");
            [KernelMethod] public static int ReadTarget(int key) => new Target(key).Read();
            [KernelMethod] public static int ReadScope(int key) => Get(key).Read();
            [UiLocalHandler] public static int Handle(int value) {{body}}
        }
        """;
}
