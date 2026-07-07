using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;

using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.CancellationParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Cancellation/timeout parity tests for side-effecting bindings.
/// Each test verifies that:
///   - Cancellation and timeout produce the expected Error.Code in both execution modes
///   - No partial side effect (sink delivery, log event) reaches the external observer
///   - The two modes are behaviorally identical for observable outputs
/// </summary>
public sealed class CompiledSideEffectCancellationTimeoutParityTests
{
    // ---------------------------------------------------------------------------
    // 1. Pre-cancelled token: message sink binding stays empty
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_blocks_message_sink_delivery_in_interpreted_mode()
    {
        // Arrange
        var messages = new InMemoryPluginMessageSink();
        using var host = CancellationMessageHost(messages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-send-interp"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        // Assert: Cancelled error, no message delivered
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Pre_cancelled_token_blocks_message_sink_delivery_in_compiled_mode_with_fallback()
    {
        // Arrange: compiled mode with AllowFallbackToInterpreter=true (current branch: side-effecting
        // bindings are not compiled directly yet; the request falls through to interpreter via fallback,
        // but the cancellation must still be honoured)
        var messages = new InMemoryPluginMessageSink();
        using var host = CancellationMessageHost(messages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-send-compiled"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true },
            cts.Token);

        // Assert: Cancelled error in whichever mode actually ran, no side effect
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    // ---------------------------------------------------------------------------
    // 2. Differential parity: pre-cancelled token on a PURE binding
    //    (pure bindings compile; this confirms the compiled kernel honours the
    //    outer CancellationToken and surfaces Cancelled — not a host failure)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_error_code_parity_on_pure_compiled_binding()
    {
        // Arrange: use a pure binding (compiles without fallback)
        using var host = CancellationPureHost();
        var module = await host.ImportJsonAsync(CancellationPureModuleJson("cancellation-pure-parity"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act: both interpreted and compiled run against the same pre-cancelled token
        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            cts.Token);

        // Assert: both modes fail with Cancelled (not HostFailure or ValidationError)
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.Cancelled, compiled.Error!.Code);

        // Actual mode labels confirm each path ran as requested
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    // ---------------------------------------------------------------------------
    // 3. Wall-time timeout: side-effecting (blocking) binding times out,
    //    no side effect delivered
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Wall_time_timeout_blocks_side_effecting_binding_delivery_no_partial_send()
    {
        // Arrange: blocking message binding that never returns unless cancelled;
        // wall-time budget of 30 ms forces a Timeout before the send completes
        var blockingMessages = new CancellationBlockingMessageSink();
        using var host = CancellationMessageHost(blockingMessages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-timeout-send"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .WithFuel(10_000)
                .WithWallTime(TimeSpan.FromMilliseconds(30))
                .Build());

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert: Timeout error, no message committed to the sink
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Empty(blockingMessages.CommittedMessages);
    }

    // ---------------------------------------------------------------------------
    // 4. Wall-time timeout parity on pure compiled binding
    //    (wall-time fires inside the compiled dispatch path)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Wall_time_timeout_parity_on_pure_compiled_binding()
    {
        // Arrange: pure slow binding that blocks until the wall-time fires
        using var host = CancellationSlowPureHost();
        var module = await host.ImportJsonAsync(CancellationSlowPureModuleJson("cancellation-wall-time-parity"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(10_000)
                .WithWallTime(TimeSpan.FromMilliseconds(50))
                .Build());

        // Act: both modes hit the wall-time deadline inside the binding
        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert: both modes surface Timeout (not HostFailure or BindingFailure)
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.Timeout, compiled.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    // ---------------------------------------------------------------------------
    // 5. Pre-cancelled token with log binding: log event not emitted on cancel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_blocks_log_binding_event_in_interpreted_mode()
    {
        // Arrange: log.info is a SideEffectingExternal binding (Audit effect)
        using var host = CancellationLogHost();
        var module = await host.ImportJsonAsync(CancellationLogModuleJson("cancellation-log-pre-cancel"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(10_000)
                .Build());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        // Assert: Cancelled, no SandboxLog audit event emitted by the binding
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "SandboxLog");
    }

    // ---------------------------------------------------------------------------
    // 6. Message binding: pre-cancelled token and compiled-mode (with fallback)
    //    agree on error code — differential parity across both observable axes
    // ---------------------------------------------------------------------------

}
