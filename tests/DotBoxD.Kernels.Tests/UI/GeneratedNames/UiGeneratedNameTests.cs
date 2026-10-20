using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.GeneratedNames;

public sealed class UiGeneratedNameTests
{
    [Theory]
    [InlineData("__sir_discard0", "{ _ = 1; return 42; }", ExecutionMode.Interpreted)]
    [InlineData("__sir_discard0", "{ _ = 1; return 42; }", ExecutionMode.Compiled)]
    [InlineData("__sir_arg", "=> new Box(42).Value;", ExecutionMode.Interpreted)]
    [InlineData("__sir_arg", "=> new Box(42).Value;", ExecutionMode.Compiled)]
    [InlineData("__sir_left", "=> 40 + Identity(2);", ExecutionMode.Interpreted)]
    [InlineData("__sir_left", "=> 40 + Identity(2);", ExecutionMode.Compiled)]
    [InlineData("__sir_src0", "{ var list = new System.Collections.Generic.List<int>(); foreach (var item in list) { return item; } return 42; }", ExecutionMode.Interpreted)]
    [InlineData("__sir_src0", "{ var list = new System.Collections.Generic.List<int>(); foreach (var item in list) { return item; } return 42; }", ExecutionMode.Compiled)]
    public async Task Unused_handler_parameters_never_collide_with_generated_locals(string name, string body, ExecutionMode mode)
    {
        var package = UiGeneratorFixture.Package($$"""
            using DotBoxD.Abstractions;
            using DotBoxD.UI;
            using DotBoxD.UI.Authoring;
            public sealed record Box(int Value);
            public static partial class Counter
            {
                [KernelMethod] public static int Identity(int value) => value;
                [UiLocalHandler] public static int Handle(string {{name}}) {{body}}
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    var input = b.State("");
                    var output = b.State(0);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
                }
            }
            """);
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(42, (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Integer);
    }
}
