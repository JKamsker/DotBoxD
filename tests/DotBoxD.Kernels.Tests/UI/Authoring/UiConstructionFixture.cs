using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Runtime;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

internal static class UiConstructionFixture
{
    private static string Handler(string type, string body) => """
        using DotBoxD.Abstractions;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        """ + type + """
        public static partial class Counter
        {
            [KernelMethod] public static Box Make(int value) => new Box(value);
            [UiLocalHandler] public static int Handle(int value)
        """ + body + "}";

    public static void AssertUnsupported(string type, string body)
    {
        var result = UiGeneratorFixture.Generate(Handler(type, body));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Result.GeneratedTrees);
    }

    public static async Task AssertMatchesNative(string type, string body, ExecutionMode mode)
    {
        var result = UiGeneratorFixture.Generate(Handler(type, body));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var expected = (int)UiGeneratorFixture.Emit(result.Compilation).GetType("Counter")!.GetMethod("Handle")!.Invoke(null, [42])!;
        var package = UiGeneratorFixture.Package(Handler(type, body) + """
            public static partial class Counter
            {
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    var input = b.State(42);
                    var output = b.State(0);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
                }
            }
            """);
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Integer);
    }
}
