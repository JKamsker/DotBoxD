using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.HostCalls;

public sealed class UiHostCallTests
{
    [Theory]
    [InlineData("=> new Target(value).Read();")]
    [InlineData("=> new ObjectTarget(value).Read();")]
    [InlineData("=> new Unforwarded(value).Read();")]
    [InlineData("=> Make(value).Read();")]
    [InlineData("=> ReadTarget(value);")]
    [InlineData("{ var scope = Get(value); value = 99; return scope.Read(); }")]
    [InlineData("{ var scope = Get(value); var alias = scope; value = 99; return alias.Read(); }")]
    [InlineData("{ var scope = Get(Next()); return scope.Read(); }")]
    [InlineData("=> GetAdjusted(value).Read();")]
    [InlineData("=> ReadScope(value);")]
    [InlineData("{ var scope = Get(value); return value; }")]
    public void Unsupported_host_receivers_and_scoped_handles_fail_closed(string body)
    {
        var generated = UiHostCallFixture.Generate(body);
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(generated.Diagnostics, d => d.Id == "DBXU001" &&
            d.GetMessage().Contains("static host binding", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(generated.Result.Results.Single().GeneratedSources);
    }

    [Theory]
    [InlineData("=> ReadKey(value);", ExecutionMode.Interpreted, 0)]
    [InlineData("=> ReadKey(value);", ExecutionMode.Compiled, 0)]
    [InlineData("{ var key = value; value = 99; return ReadKey(key); }", ExecutionMode.Interpreted, 0)]
    [InlineData("{ var key = value; value = 99; return ReadKey(key); }", ExecutionMode.Compiled, 0)]
    [InlineData("{ var key = Next(); return ReadKey(key); }", ExecutionMode.Interpreted, 1)]
    [InlineData("{ var key = Next(); return ReadKey(key); }", ExecutionMode.Compiled, 1)]
    public Task Explicit_host_arguments_preserve_capture_and_both_kernel_modes(string body, ExecutionMode mode, int calls)
        => UiHostCallFixture.AssertMatchesNative(body, mode, calls);
}
