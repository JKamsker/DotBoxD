using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiOperandOrderTests
{
    [Theory]
    [InlineData("=> Next() - Identity(Next());", false, 2, ExecutionMode.Interpreted)]
    [InlineData("=> Next() - Identity(Next());", false, 2, ExecutionMode.Compiled)]
    [InlineData("=> Next() - (Identity(Next()) - Next());", false, 3, ExecutionMode.Interpreted)]
    [InlineData("=> Next() - (Identity(Next()) - Next());", false, 3, ExecutionMode.Compiled)]
    [InlineData("=> Identity(Next()) - Identity(Next());", false, 2, ExecutionMode.Interpreted)]
    [InlineData("=> Identity(Next()) - Identity(Next());", false, 2, ExecutionMode.Compiled)]
    [InlineData("=> Next().ToString(CultureInfo.InvariantCulture) + Text(Next());", true, 2, ExecutionMode.Interpreted)]
    [InlineData("=> Next().ToString(CultureInfo.InvariantCulture) + Text(Next());", true, 2, ExecutionMode.Compiled)]
    public async Task Eager_operands_preserve_native_host_call_order_in_both_kernel_modes(
        string body, bool text, int calls, ExecutionMode mode)
    {
        var expected = UiOperandOrderFixture.Native(body, text);
        var (binding, counter) = UiOperandOrderFixture.NextBinding();
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(binding).UseCompilerIfAvailable());
        await using var session = await Host(sandbox, mode).InstallAsync(
            UiOperandOrderFixture.Package(body, text), new RecordingUiRenderer());
        var value = (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value;
        Assert.Equal(expected, text ? value.Text : (object)value.Integer);
        Assert.Equal(calls, counter());
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Earlier_operand_failure_prevents_later_host_calls(ExecutionMode mode)
    {
        const string body = "=> 10 / value + Identity(Next());";
        Assert.IsType<DivideByZeroException>(Assert.Throws<TargetInvocationException>(
            () => UiOperandOrderFixture.Native(body)).InnerException);
        var (binding, counter) = UiOperandOrderFixture.NextBinding();
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings().AddBinding(binding).UseCompilerIfAvailable());
        await using var session = await Host(sandbox, mode).InstallAsync(
            UiOperandOrderFixture.Package(body), new RecordingUiRenderer());
        await Assert.ThrowsAsync<UiValidationException>(() => session.DispatchAsync(1).AsTask());
        Assert.Equal(0, counter());
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }

    private static UiHost Host(SandboxHost sandbox, ExecutionMode mode) => new(sandbox,
        SandboxPolicyBuilder.Create().Grant("review.next", new { }, SandboxEffect.HostStateWrite).Build(),
        execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
}
