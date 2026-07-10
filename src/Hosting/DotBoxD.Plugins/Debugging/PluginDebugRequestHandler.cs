using System.Text.Json;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugRequestHandler(PluginDebugSession session)
{
    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var request = PluginDebugProtocol.Decode(message, session.Options.MaxMessageBytes);
        session.Authenticate(request.SessionToken);
        if (request.Version != PluginDebugProtocol.Version)
        {
            return ValueTask.FromResult(Error(
                request,
                "unsupportedVersion",
                $"Debug protocol version {request.Version} is not supported."));
        }

        session.RenewLease();
        var response = request.Kind switch
        {
            PluginDebugCommands.Initialize => Initialize(request),
            PluginDebugCommands.Attach => Attach(request),
            PluginDebugCommands.SetBreakpoints => SetBreakpoints(request),
            PluginDebugCommands.Pause => Pause(request),
            PluginDebugCommands.Continue => Continue(request),
            PluginDebugCommands.Heartbeat => Success(request, new { }),
            PluginDebugCommands.Disconnect => Disconnect(request),
            _ => Error(request, "unsupportedCommand", $"Debug command '{request.Kind}' is not supported.")
        };
        return ValueTask.FromResult(response);
    }

    private byte[] Initialize(PluginDebugEnvelope request)
        => Success(
            request,
            new
            {
                supported = session.Options.Enabled,
                protocolVersion = PluginDebugProtocol.Version,
                supportedVersions = new[] { PluginDebugProtocol.Version },
                commands = new[]
                {
                    PluginDebugCommands.Initialize,
                    PluginDebugCommands.Attach,
                    PluginDebugCommands.SetBreakpoints,
                    PluginDebugCommands.Pause,
                    PluginDebugCommands.Continue,
                    PluginDebugCommands.Heartbeat,
                    PluginDebugCommands.Disconnect
                },
                defaultPauseScope = ScopeName(session.Options.DefaultPauseScope),
                allowedPauseScopes = session.AllowedPauseScopes.Select(ScopeName).Order().ToArray(),
                stopLeaseMilliseconds = checked((long)session.Options.StopLease.TotalMilliseconds),
                limits = new
                {
                    snapshotBytes = session.Options.MaxSnapshotBytes,
                    expressionLength = session.Options.MaxExpressionLength,
                    assemblyUploadBytes = session.Options.MaxAssemblyUploadBytes,
                    messageBytes = session.Options.MaxMessageBytes
                }
            });

    private byte[] Attach(PluginDebugEnvelope request)
    {
        if (!session.Options.Enabled)
        {
            return Error(request, "debuggingDisabled", "Remote kernel debugging is disabled by the host.");
        }

        if (!TryReadScope(request.Payload, out var scope, out var scopeError))
        {
            return Error(request, "invalidPauseScope", scopeError!);
        }

        if (!session.AllowedPauseScopes.Contains(scope))
        {
            return Error(request, "pauseScopeDenied", "The requested pause scope is not allowed by the host.");
        }

        if (!session.TryAttach(scope))
        {
            return Error(request, "debuggerAlreadyAttached", "Another debugger is attached to this server.");
        }

        return Success(request, new { pauseScope = ScopeName(scope) });
    }

    private byte[] Disconnect(PluginDebugEnvelope request)
    {
        session.DetachFromClient();
        return Success(request, new { });
    }

    private byte[] SetBreakpoints(PluginDebugEnvelope request)
    {
        if (!TryReadString(request.Payload, "pluginId", out var pluginId) ||
            request.Payload.ValueKind != JsonValueKind.Object ||
            !request.Payload.TryGetProperty("nodeIds", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return Error(request, "invalidArguments", "setBreakpoints requires pluginId and a nodeIds array.");
        }

        try
        {
            var nodeIds = values.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty)
                .ToArray();
            var parsed = session.ExecutionState.SetBreakpoints(pluginId!, nodeIds);
            return Success(
                request,
                new
                {
                    breakpoints = parsed.Select(node => new
                    {
                        nodeId = node.Value,
                        verified = session.IsBreakpointVerified(pluginId!, node)
                    }).ToArray()
                });
        }
        catch (ArgumentException exception)
        {
            return Error(request, "invalidBreakpoint", exception.Message);
        }
    }

    private byte[] Pause(PluginDebugEnvelope request)
    {
        if (!session.IsAttached)
        {
            return Error(request, "notAttached", "No debugger is attached to this session.");
        }

        session.ExecutionState.RequestPause();
        return Success(request, new { });
    }

    private byte[] Continue(PluginDebugEnvelope request)
    {
        if (!TryReadString(request.Payload, "runId", out var runId) ||
            !session.ExecutionState.ContainsStopped(runId!))
        {
            return Error(request, "staleRun", "The requested execution is not stopped in this plugin session.");
        }

        return session.Resume(runId!)
            ? Success(request, new { runId })
            : Error(request, "staleRun", "The requested execution is no longer stopped.");
    }

    private byte[] Success(PluginDebugEnvelope request, object body)
        => Response(request, request.Kind + "Response", new { success = true, body });

    private byte[] Error(PluginDebugEnvelope request, string code, string message)
        => Response(request, "error", new { success = false, error = new { code, message } });

    private byte[] Response(PluginDebugEnvelope request, string kind, object payload)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                kind,
                request.Id,
                session.SessionToken,
                JsonSerializer.SerializeToElement(payload)),
            session.Options.MaxMessageBytes);

    private bool TryReadScope(JsonElement payload, out KernelDebugPauseScope scope, out string? error)
    {
        scope = session.Options.DefaultPauseScope;
        error = null;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("pauseScope", out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String || !TryParseScope(value.GetString(), out scope))
        {
            error = "The requested pauseScope must be server, pluginSession, or execution.";
            return false;
        }

        return true;
    }

    private static bool TryReadString(JsonElement payload, string name, out string? value)
    {
        value = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseScope(string? value, out KernelDebugPauseScope scope)
    {
        scope = value switch
        {
            "server" => KernelDebugPauseScope.Server,
            "pluginSession" => KernelDebugPauseScope.PluginSession,
            "execution" => KernelDebugPauseScope.Execution,
            _ => (KernelDebugPauseScope)(-1)
        };
        return Enum.IsDefined(scope);
    }

    private static string ScopeName(KernelDebugPauseScope scope)
        => scope switch
        {
            KernelDebugPauseScope.Server => "server",
            KernelDebugPauseScope.PluginSession => "pluginSession",
            KernelDebugPauseScope.Execution => "execution",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported debug pause scope.")
        };
}
