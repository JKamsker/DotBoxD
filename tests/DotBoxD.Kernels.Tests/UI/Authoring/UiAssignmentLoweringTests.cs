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
}
