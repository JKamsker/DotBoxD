using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.CancellationParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectPreCancellationParityTests
{
    [Fact]
    public async Task Pre_cancelled_token_error_code_matches_in_both_modes_for_message_binding()
    {
        // Arrange: run the same module interpreted vs compiled (with fallback)
        // A pre-cancelled token must produce Cancelled in both paths.
        const string moduleJson = """
        {
          "id": "cancellation-parity-message",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [{ "string": "player-1" }, { "string": "ping" }]
                  }
                }
              ]
            }
          ]
        }
        """;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var messagesI = new InMemoryPluginMessageSink();
        var hostI = CancellationMessageHost(messagesI);
        var moduleI = await hostI.ImportJsonAsync(moduleJson);
        var planI = await hostI.PrepareAsync(moduleI, CancellationMessagePolicy());

        var messagesC = new InMemoryPluginMessageSink();
        var hostC = CancellationMessageHost(messagesC);
        var moduleC = await hostC.ImportJsonAsync(moduleJson);
        var planC = await hostC.PrepareAsync(moduleC, CancellationMessagePolicy());

        // Act
        var interpreted = await hostI.ExecuteAsync(
            planI, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        var compiled = await hostC.ExecuteAsync(
            planC, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true },
            cts.Token);

        // Assert: both fail with same error code
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(interpreted.Error!.Code, compiled.Error!.Code);
        Assert.Equal(SandboxErrorCode.Cancelled, interpreted.Error.Code);

        // Neither sink received a message
        Assert.Empty(messagesI.Messages);
        Assert.Empty(messagesC.Messages);
    }

    // ---------------------------------------------------------------------------
    // 7. Cancellation mid-run (not pre-cancelled) via CancellationTokenSource
    //    for interpreted mode with a side-effecting blocking sink
    // ---------------------------------------------------------------------------

}
