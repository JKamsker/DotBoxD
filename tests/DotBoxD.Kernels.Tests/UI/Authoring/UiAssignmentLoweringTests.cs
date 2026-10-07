using DotBoxD.UI;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiAssignmentLoweringTests
{
    [Theory]
    [InlineData("public static int Sink { get => 0; set => throw new System.Exception(); }", "Sink = value;")]
    [InlineData("public static int Sink;", "Sink = value;")]
    [InlineData("public static int Sink { get => 0; set => throw new System.Exception(); }", "Sink += value;")]
    [InlineData("public static int Sink;", "Sink += value;")]
    [InlineData("public static int Sink { get => 0; set => throw new System.Exception(); }", "Sink++;")]
    [InlineData("public static int Sink;", "--Sink;")]
    public void Nonlocal_handler_writes_fail_closed_at_compile_time(string member, string write)
    {
        var result = UiGeneratorFixture.Generate("using DotBoxD.UI.Authoring; public static partial class Handlers { " + member +
            " [UiLocalHandler] public static int Handle(int value) { " + write + " return value; } }");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Contains("host binding", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(result.Result.GeneratedTrees);
    }

    [Theory]
    [InlineData("value = value + 1; return value;", 43)]
    [InlineData("var result = value; result += 2; result++; --result; return result;", 44)]
    [InlineData("_ = value + 2; return value + 1;", 43)]
    [InlineData("_ = \"discarded\"; _ = value; return value;", 42)]
    [InlineData("var __sir_discard0 = value; _ = \"discarded\"; _ = value; return __sir_discard0 + 1;", 43)]
    [InlineData("var _ = value; _ = value + 1; return _;", 43)]
    public async Task Local_parameter_and_discard_writes_keep_their_execution_semantics(string body, int expected)
    {
        var package = UiGeneratorFixture.Package("""
            using DotBoxD.UI;
            using DotBoxD.UI.Authoring;
            public static partial class Counter
            {
                [UiLocalHandler] public static int Handle(int value) {
            """ + body + """
                }
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    var state = b.State(42);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), state), state));
                }
            }
            """);
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State[0].Value.Integer);
    }

    [Theory]
    [InlineData("+=")]
    [InlineData("-=")]
    [InlineData("*=")]
    [InlineData("/=")]
    [InlineData("%=")]
    public void Narrowing_compound_handler_writes_fail_closed_at_compile_time(string operation)
    {
        var result = UiGeneratorFixture.Generate("using DotBoxD.UI.Authoring; public static partial class Handlers { " +
            "[UiLocalHandler] public static int Handle(int value) { byte result = 200; byte step = 100; result " + operation + " step; return result; } }");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Result.GeneratedTrees);
    }

    [Theory]
    [InlineData("+= value", 84d)]
    [InlineData("-= value", 0d)]
    [InlineData("*= 2", 84d)]
    [InlineData("/= 2", 21d)]
    [InlineData("%= 5", 2d)]
    public async Task Widening_compound_handler_writes_preserve_numeric_execution(string operation, double expected)
    {
        var package = UiGeneratorFixture.Package("""
            using DotBoxD.UI;
            using DotBoxD.UI.Authoring;
            public static partial class Counter
            {
                [UiLocalHandler] public static double Handle(int value) { double result = value;
            """ + "result " + operation + "; return result; }" + """
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    return b.Build(b.Progress(b.Kernel(HandleUiKernel(), b.State(42))));
                }
            }
            """);
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(package, renderer);
        Assert.Equal(expected, Assert.Single(renderer.Initial, p => p.PropertyId == UiPropertyId.Value).Value.Number);
    }
}
