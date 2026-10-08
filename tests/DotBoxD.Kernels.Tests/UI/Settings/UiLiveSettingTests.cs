using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests.UI.Authoring;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Settings;

public sealed class UiLiveSettingTests
{
    [Theory]
    [InlineData("() => Setting;")]
    [InlineData("(int value) => Setting + value;")]
    [InlineData("() => ReadSetting();")]
    [InlineData("(int Setting) => ReadSetting();")]
    [InlineData("() { var Setting = 7; return ReadSetting() + Setting; }")]
    [InlineData("(int Setting) => ReadNested();")]
    public void Live_settings_fail_closed_in_UI_handlers_and_inlined_helpers(string handler)
    {
        var generated = UiGeneratorFixture.Generate(Source(handler));
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(generated.Diagnostics, d => d.Id == "DBXU001" &&
            d.GetMessage().Contains("live setting", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(generated.Result.Results.Single().GeneratedSources);
    }

    [Theory]
    [InlineData("(int Setting) => Setting;", ExecutionMode.Interpreted)]
    [InlineData("(int Setting) => Setting;", ExecutionMode.Compiled)]
    [InlineData("(int Setting) => Identity(Setting);", ExecutionMode.Interpreted)]
    [InlineData("(int Setting) => Identity(Setting);", ExecutionMode.Compiled)]
    public Task Parameters_named_like_live_settings_preserve_both_kernel_modes(string handler, ExecutionMode mode)
        => AssertMatchesNative(handler, mode);

    private static async Task AssertMatchesNative(string handler, ExecutionMode mode)
    {
        var source = Source(handler) + """
            public static partial class Counter
            {
                public static UiPackage Package()
                {
                    var builder = new UiBuilder();
                    var input = builder.State(7);
                    var output = builder.State(0);
                    return builder.Build(builder.Button("Run", builder.Kernel(HandleUiKernel(), input), output));
                }
            }
            """;
        var generated = UiGeneratorFixture.Generate(source);
        Assert.Empty(generated.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(generated.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var type = UiGeneratorFixture.Emit(generated.Output).GetType("Counter")!;
        var expected = (int)type.GetMethod("Handle")!.Invoke(null, [7])!;
        var package = (UiPackage)type.GetMethod("Package")!.Invoke(null, null)!;
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        var actual = (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Integer;
        Assert.Equal(expected, actual);
    }

    private static string Source(string handler) => $$"""
        using DotBoxD.Abstractions;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public static partial class Counter
        {
            [LiveSetting] public static int Setting => 42;
            [KernelMethod] public static int ReadSetting() => Setting;
            [KernelMethod] public static int ReadNested() => ReadSetting();
            [KernelMethod] public static int Identity(int Setting) => Setting;
            [UiLocalHandler] public static int Handle{{handler}}
        }
        """;
}
