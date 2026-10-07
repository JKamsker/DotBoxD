using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Runtime;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

internal static class UiBooleanGeneratorFixture
{
    public static void AssertUnsupported(string body)
    {
        var generated = UiGeneratorFixture.Generate(Handler(body));
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generated.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(generated.Result.GeneratedTrees);
    }

    public static async Task AssertExecutes(string body, bool expected, ExecutionMode mode)
    {
        var package = UiGeneratorFixture.Package(PackageSource(body));
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Boolean);
    }

    private static string Handler(string body) => """
        using System.Collections.Generic;
        using DotBoxD.Abstractions;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public sealed record ValueRecord(int Value);
        public readonly record struct ValueStruct(int Value);
        public readonly record struct OperatorValue(int Value)
        {
            public static int operator +(OperatorValue left, OperatorValue right) => left.Value + right.Value;
            public static OperatorValue operator *(OperatorValue left, OperatorValue right) => new(left.Value * right.Value);
            public static int operator -(OperatorValue value) => -value.Value;
        }
        public static partial class Counter
        {
            [KernelMethod] public static bool Positive(int x) => x > 0;
            [UiLocalHandler] public static bool Handle(int value)
        """ + body + "}";

    internal static string PackageSource(string body) => Handler(body) + """
        public static partial class Counter
        {
            public static UiPackage Package()
            {
                var b = new UiBuilder();
                var input = b.State(0);
                var output = b.State(false);
                return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
            }
        }
        """;
}
