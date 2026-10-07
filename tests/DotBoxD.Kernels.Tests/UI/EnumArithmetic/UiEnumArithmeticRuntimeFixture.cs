using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.EnumArithmetic;

internal static class UiEnumArithmeticRuntimeFixture
{
    public static async Task AssertExecutes(string source, bool expected, ExecutionMode mode)
    {
        var package = UiGeneratorFixture.Package(source + """
            public static partial class Counter
            {
                public static DotBoxD.UI.UiPackage Package()
                {
                    var b = new DotBoxD.UI.Authoring.UiBuilder();
                    var input = b.State(0);
                    var output = b.State(false);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
                }
            }
            """);
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Boolean);
    }
}
