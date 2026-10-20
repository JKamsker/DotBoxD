using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.EnumArithmetic;

public sealed class UiEnumArithmeticTests
{
    [Theory]
    [InlineData("byte", "{ var current = Tiny.Max; byte step = 1; var next = current + step; return next == Tiny.Zero; }")]
    [InlineData("sbyte", "{ var current = Tiny.Max; sbyte step = 1; var next = current + step; return next == Tiny.Zero; }")]
    [InlineData("short", "{ var current = Tiny.Max; short step = 1; var next = current + step; return next == Tiny.Zero; }")]
    [InlineData("ushort", "{ var current = Tiny.Max; ushort step = 1; var next = current + step; return next == Tiny.Zero; }")]
    [InlineData("uint", "{ var zero = Tiny.Zero; var one = Tiny.One; var max = Tiny.Max; return zero - one == max - zero; }")]
    [InlineData("byte", "{ var zero = Tiny.Zero; var one = Tiny.One; var difference = zero - one; return difference == 255; }")]
    [InlineData("sbyte", "{ var current = Tiny.Max; sbyte step = 1; current += step; return current == Tiny.Zero; }")]
    [InlineData("byte", "{ var current = Tiny.Zero; byte step = 1; current -= step; return current == Tiny.Max; }")]
    [InlineData("byte", "{ var current = Tiny.Max; current++; return current == Tiny.Zero; }")]
    [InlineData("byte", "{ var current = Tiny.Zero; --current; return current == Tiny.Max; }")]
    [InlineData("byte", "{ var current = Tiny.Max; byte step = 1; return Add(current, step) == Tiny.Zero; }")]
    public void Enum_arithmetic_requiring_underlying_width_conversion_fails_closed(string underlying, string body)
    {
        var generated = UiEnumArithmeticFixture.Generate(underlying, body);
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(generated.Diagnostics, d => d.Id == "DBXU001");
        Assert.Empty(generated.Result.GeneratedTrees);
    }

    [Theory]
    [InlineData("byte", "{ var current = Tiny.Max; return current == Tiny.Max; }", ExecutionMode.Interpreted)]
    [InlineData("byte", "{ var current = Tiny.Max; return current == Tiny.Max; }", ExecutionMode.Compiled)]
    [InlineData("byte", "{ var current = Tiny.Max; return current > Tiny.Zero; }", ExecutionMode.Interpreted)]
    [InlineData("byte", "{ var current = Tiny.Max; return current > Tiny.Zero; }", ExecutionMode.Compiled)]
    [InlineData("int", "{ var current = Tiny.One; var next = current + 1; return next == (Tiny)2; }", ExecutionMode.Interpreted)]
    [InlineData("int", "{ var current = Tiny.One; var next = current + 1; return next == (Tiny)2; }", ExecutionMode.Compiled)]
    public Task Enum_comparisons_and_matching_width_arithmetic_preserve_both_modes(string underlying, string body, ExecutionMode mode)
        => UiEnumArithmeticFixture.AssertMatchesNative(underlying, body, mode);
}
