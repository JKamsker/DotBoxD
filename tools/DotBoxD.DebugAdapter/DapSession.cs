using System.Text.Json;
using DotBoxD.DebugAdapter.Diagnostics;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapSession(DapConnection connection) : IAsyncDisposable
{
    private BridgeClient? _bridge;
    private DapInspectionHandler? _inspection;
    private DapBreakpointHandler? _breakpoints;
    private string? _pluginId;
    private bool _finished;
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!_finished && !cancellationToken.IsCancellationRequested)
        {
            using var request = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            await HandleSafelyAsync(request.RootElement, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_bridge is not null)
        {
            await _bridge.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask HandleSafelyAsync(JsonElement request, CancellationToken cancellationToken)
    {
        AdapterDiagnostics.Write("request " + request.GetProperty("command").GetString());
        try
        {
            await HandleAsync(request, cancellationToken).ConfigureAwait(false);
            AdapterDiagnostics.Write("completed " + request.GetProperty("command").GetString());
        }
        catch (DebugAdapterException exception)
        {
            AdapterDiagnostics.Write("adapter error " + exception.Code + ": " + exception.Message);
            await connection.RespondAsync(
                    request,
                    success: false,
                    body: new { error = new { id = exception.Code, format = exception.Message } },
                    message: exception.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AdapterDiagnostics.Write("unhandled error " + exception);
            await connection.RespondAsync(request, false, null, exception.Message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var command = request.GetProperty("command").GetString()!;
        if (_inspection?.Handles(command) == true)
        {
            await _inspection.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (command)
        {
            case "initialize":
                await InitializeAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case "attach":
            case "launch":
                await AttachAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case "setBreakpoints":
                await RequireBreakpoints().HandleAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case "configurationDone":
                await ConfigurationDoneAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case "continue":
            case "next":
            case "stepIn":
            case "stepOut":
            case "pause":
                await ControlAsync(request, command, cancellationToken).ConfigureAwait(false);
                break;
            case "disconnect":
            case "terminate":
                await DisconnectAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new DebugAdapterException("unsupportedCommand", $"DAP command '{command}' is not supported.");
        }
    }

    private async ValueTask InitializeAsync(JsonElement request, CancellationToken cancellationToken)
    {
        await connection.RespondAsync(
                request,
                true,
                new
                {
                    supportsConfigurationDoneRequest = true,
                    supportsConditionalBreakpoints = true,
                    supportsHitConditionalBreakpoints = true,
                    supportsLogPoints = true,
                    supportsEvaluateForHovers = true,
                    supportsSetVariable = true,
                    supportsSetExpression = true,
                    supportsTerminateRequest = true,
                    supportsLoadedSourcesRequest = false,
                    exceptionBreakpointFilters = Array.Empty<object>()
                },
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask AttachAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (_bridge is not null)
        {
            throw new DebugAdapterException("alreadyAttached", "The adapter is already attached.");
        }

        var arguments = request.GetProperty("arguments");
        _pluginId = arguments.TryGetProperty("pluginId", out var pluginId) && pluginId.ValueKind == JsonValueKind.String
            ? pluginId.GetString() ?? string.Empty
            : string.Empty;
        _bridge = await ConnectBridgeAsync(arguments, cancellationToken).ConfigureAwait(false);
        _inspection = new DapInspectionHandler(connection, _bridge, _pluginId);
        _breakpoints = new DapBreakpointHandler(connection, _bridge, _pluginId);
        _bridge.EventReceiver = _inspection.OnRemoteEventAsync;
        _bridge.SourcesChangedReceiver = _breakpoints.OnSourcesChangedAsync;
        _ = await _bridge.RemoteAsync(PluginDebugCommands.Initialize, null, cancellationToken).ConfigureAwait(false);
        var pauseScope = arguments.TryGetProperty("pauseScope", out var scope)
            ? scope.GetString()
            : null;
        _ = await _bridge.RemoteAsync(
                PluginDebugCommands.Attach,
                pauseScope is null ? null : new { pauseScope },
                cancellationToken)
            .ConfigureAwait(false);
        await connection.RespondAsync(request, true, new { }, null, cancellationToken).ConfigureAwait(false);
        await connection.EventAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ConfigurationDoneAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var response = await RequireBridge().SendAsync("configurationDone", null, cancellationToken)
            .ConfigureAwait(false);
        EnsureBridgeSuccess(response);
        await connection.RespondAsync(request, true, new { }, null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ControlAsync(
        JsonElement request,
        string command,
        CancellationToken cancellationToken)
    {
        var bridge = RequireBridge();
        string remoteCommand;
        object? payload;
        int? controlledThreadId = null;
        if (command == "pause")
        {
            remoteCommand = PluginDebugCommands.Pause;
            payload = null;
        }
        else
        {
            var threadId = request.GetProperty("arguments").GetProperty("threadId").GetInt32();
            controlledThreadId = threadId;
            remoteCommand = command switch
            {
                "next" => PluginDebugCommands.StepOver,
                "stepIn" => PluginDebugCommands.StepIn,
                "stepOut" => PluginDebugCommands.StepOut,
                _ => PluginDebugCommands.Continue
            };
            payload = new { runId = RequireInspection().RunId(threadId) };
            RequireInspection().BeginResume();
        }

        try
        {
            _ = await bridge.RemoteAsync(remoteCommand, payload, cancellationToken).ConfigureAwait(false);
            await connection.RespondAsync(
                    request,
                    true,
                    command == "continue" ? new { allThreadsContinued = false } : new { },
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (controlledThreadId is { } resumedThreadId)
            {
                RequireInspection().InvalidateStoppedState(
                    resumedThreadId,
                    preserveThreadIdentity: command != "continue");
                await connection.EventAsync(
                        "continued",
                        new { threadId = resumedThreadId, allThreadsContinued = false },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (controlledThreadId is not null)
            {
                await RequireInspection().CompleteResumeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask DisconnectAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (_bridge is not null)
        {
            try
            {
                _ = await _bridge.RemoteAsync(PluginDebugCommands.Disconnect, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DebugAdapterException or IOException)
            {
                // A lost remote session is already disconnected.
            }
        }

        _inspection?.InvalidateAllStoppedState();
        await connection.RespondAsync(request, true, new { }, null, cancellationToken).ConfigureAwait(false);
        await connection.EventAsync("terminated", new { }, cancellationToken).ConfigureAwait(false);
        _finished = true;
    }

    private BridgeClient RequireBridge()
        => _bridge ?? throw new DebugAdapterException("notAttached", "Attach the adapter before configuring debugging.");

    private static Task<BridgeClient> ConnectBridgeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.TryGetProperty("processId", out var processId))
        {
            var value = processId.ValueKind == JsonValueKind.Number
                ? processId.GetInt32()
                : int.Parse(processId.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            return BridgeClient.ConnectByProcessIdAsync(value, TimeSpan.FromSeconds(30), cancellationToken);
        }

        return BridgeClient.ConnectAsync(
            RequiredString(arguments, "pipeName"),
            RequiredString(arguments, "discoveryToken"),
            cancellationToken);
    }

    private DapInspectionHandler RequireInspection()
        => _inspection ?? throw new DebugAdapterException("notAttached", "Attach the adapter before controlling execution.");

    private DapBreakpointHandler RequireBreakpoints()
        => _breakpoints ?? throw new DebugAdapterException("notAttached", "Attach the adapter before setting breakpoints.");

    private static string RequiredString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new DebugAdapterException("invalidArguments", $"{name} is null.")
            : throw new DebugAdapterException("invalidArguments", $"{name} is required.");

    private static void EnsureBridgeSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException("bridgeError", response.GetProperty("error").GetString()!);
        }
    }
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var diagnosticArgument = Array.IndexOf(args, "--diagnostic-log");
        if (diagnosticArgument >= 0 && diagnosticArgument + 1 < args.Length)
        {
            AdapterDiagnostics.Configure(args[diagnosticArgument + 1]);
        }

        var connection = new DapConnection(System.Console.OpenStandardInput(), System.Console.OpenStandardOutput());
        await using var session = new DapSession(connection);
        await session.RunAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }
}
