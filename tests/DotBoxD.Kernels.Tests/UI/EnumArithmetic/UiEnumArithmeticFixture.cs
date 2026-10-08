using DotBoxD.Kernels.Tests.UI.Authoring;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.EnumArithmetic;

internal static class UiEnumArithmeticFixture
{
    public static UiGeneratorFixture.Generation Generate(string underlying, string body) => UiGeneratorFixture.Generate(Source(underlying, body));
    public static async Task AssertMatchesNative(string underlying, string body, ExecutionMode mode)
    {
        var generated = Generate(underlying, body);
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var type = UiGeneratorFixture.Emit(generated.Compilation).GetType("Counter")!;
        var expected = (bool)type.GetMethod("Handle")!.Invoke(null, [0])!;
        await UiEnumArithmeticRuntimeFixture.AssertExecutes(Source(underlying, body), expected, mode);
    }

    private static string Source(string underlying, string body) => $$"""
        using DotBoxD.Abstractions;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public enum Tiny : {{underlying}} { Zero = 0, One = 1, Max = {{underlying}}.MaxValue }
        public static partial class Counter
        {
            [KernelMethod] public static Tiny Add(Tiny current, {{underlying}} step) => current + step;
            [UiLocalHandler] public static bool Handle(int value) {{body}}
        }
        """;
}
