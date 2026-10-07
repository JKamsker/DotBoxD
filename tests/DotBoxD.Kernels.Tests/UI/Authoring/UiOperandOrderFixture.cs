using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

internal static class UiOperandOrderFixture
{
    private static string Handler(string body, bool text) => """
        using System.Globalization;
        using System.Collections.Generic;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public static partial class Counter
        {
            private static int next;
            [HostBinding("review.next", "review.next", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static int Next() => ++next;
            [HostBinding("review.values", "review.order", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static List<int> Values() => new() { next };
            [HostBinding("review.map", "review.order", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static Dictionary<int, int> Mapping() => new() { [0] = next };
            [HostBinding("review.keys", "review.order", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static Dictionary<int, int> Keys() => new() { [next] = next };
            [HostBinding("review.index", "review.order", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            public static int Index() { next++; return 0; }
            [KernelMethod] public static int Identity(int value) => value;
            [KernelMethod] public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
            [UiLocalHandler] public static
        """ + (text ? " string " : " int ") + " Handle(int value) " + body + "}";

    public static object Native(string body, bool text = false)
    {
        var generated = UiGeneratorFixture.Generate(Handler(body, text));
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return UiGeneratorFixture.Emit(generated.Compilation).GetType("Counter")!.GetMethod("Handle")!.Invoke(null, [0])!;
    }

    public static UiPackage Package(string body, bool text = false) => UiGeneratorFixture.Package(Handler(body, text) + $$"""
        public static partial class Counter
        {
            public static UiPackage Package()
            {
                var b = new UiBuilder();
                var input = b.State(0);
                var output = b.State({{(text ? "\"\"" : "0")}});
                return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
            }
        }
        """);

    public static (BindingDescriptor Binding, Func<int> Calls) NextBinding()
    {
        var calls = 0;
        var binding = new BindingDescriptor("review.next", SemVersion.One, [], SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite, "review.next", BindingCostModel.Fixed(1),
            AuditLevel.PerResource, BindingSafety.SideEffectingExternal,
            (context, _, _) =>
            {
                var started = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(context.RunId, "BindingCall", started, true,
                    BindingId: "review.next", CapabilityId: "review.next", Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "counter", Fields: context.BindingAuditFields("ui-order", started)));
                return ValueTask.FromResult(SandboxValue.FromInt32(++calls));
            }, CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });
        return (binding, () => calls);
    }

    public static (BindingDescriptor[] Bindings, List<string> Calls) CollectionBindings(bool failReceiver = false)
    {
        var counter = 0;
        var calls = new List<string>();
        BindingDescriptor Create(string id, SandboxType result, Func<SandboxValue> invoke) => new(
            id, SemVersion.One, [], result, SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            "review.order", BindingCostModel.Fixed(1), AuditLevel.PerResource, BindingSafety.SideEffectingExternal,
            (context, _, _) =>
            {
                calls.Add(id);
                var started = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(context.RunId, "BindingCall", started, true,
                    BindingId: id, CapabilityId: "review.order", Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "counter", Fields: context.BindingAuditFields("ui-order", started)));
                if (failReceiver && id != "review.index")
                {
                    throw new InvalidOperationException("receiver failed");
                }

                return ValueTask.FromResult(invoke());
            }, CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });
        var bindings = new[]
        {
            Create("review.values", SandboxType.List(SandboxType.I32),
                () => SandboxValue.FromList([SandboxValue.FromInt32(counter)], SandboxType.I32)),
            Create("review.map", SandboxType.Map(SandboxType.I32, SandboxType.I32), () => MapValue(0, counter)),
            Create("review.keys", SandboxType.Map(SandboxType.I32, SandboxType.I32), () => MapValue(counter, counter)),
            Create("review.index", SandboxType.I32, () => { counter++; return SandboxValue.FromInt32(0); })
        };
        return (bindings, calls);
    }

    private static SandboxValue MapValue(int key, int value) => SandboxValue.FromMap(
        new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromInt32(key)] = SandboxValue.FromInt32(value) },
        SandboxType.I32, SandboxType.I32);
}
