using System.Text.Json;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugRequestHandler(PluginDebugSession session)
{
    private readonly PluginDebugExecutionRequestHandler _execution = new(session);
    private readonly PluginDebugAssemblyUploadHandler _assemblyUpload = new(session);

    public async ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var request = PluginDebugProtocol.Decode(message, session.Options.MaxMessageBytes);
        session.Authenticate(request.SessionToken);
        if (request.Version != PluginDebugProtocol.Version)
        {
            return Error(
                request,
                "unsupportedVersion",
                $"Debug protocol version {request.Version} is not supported.");
        }

        session.RenewLease();
        var response = request.Kind switch
        {
            PluginDebugCommands.Initialize => Initialize(request),
            PluginDebugCommands.Attach => Attach(request),
            PluginDebugCommands.Heartbeat => Success(request, new { }),
            PluginDebugCommands.Disconnect => Disconnect(request),
            _ => null
        };
        if (response is not null)
        {
            return response;
        }

        return await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private byte[] Initialize(PluginDebugEnvelope request)
        => Success(
            request,
            new
            {
                supported = session.Options.Enabled,
                protocolVersion = PluginDebugProtocol.Version,
                supportedVersions = new[] { PluginDebugProtocol.Version },
                commands = SupportedCommands(),
                defaultPauseScope = ScopeName(session.Options.DefaultPauseScope),
                allowedPauseScopes = session.AllowedPauseScopes.Select(ScopeName).Order().ToArray(),
                stopLeaseMilliseconds = checked((long)session.Options.StopLease.TotalMilliseconds),
                evaluator = new
                {
                    id = session.Options.EvaluatorProvider.Id,
                    trustProfile = session.Options.EvaluatorProvider.TrustProfile.ToString(),
                    supportsAwait = session.Options.EvaluatorProvider.SupportsAwait,
                    supportsAssemblyUpload = SupportsAssemblyUpload()
                },
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

    private async ValueTask<byte[]> ExecuteAsync(
        PluginDebugEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == PluginDebugCommands.UploadAssembly)
        {
            return EncodeResult(request, _assemblyUpload.Handle(request.Payload));
        }

        var result = await _execution.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return Error(request, "unsupportedCommand", $"Debug command '{request.Kind}' is not supported.");
        }

        return EncodeResult(request, result);
    }

    private byte[] EncodeResult(PluginDebugEnvelope request, PluginDebugHandlerResult result)
        => result.Succeeded
            ? Success(request, result.Body!)
            : Error(request, result.Code!, result.Message!);

    private string[] SupportedCommands()
    {
        string[] commands =
        [
            PluginDebugCommands.Initialize,
            PluginDebugCommands.Attach,
            PluginDebugCommands.SetBreakpoints,
            PluginDebugCommands.Pause,
            PluginDebugCommands.Continue,
            PluginDebugCommands.StepIn,
            PluginDebugCommands.StepOver,
            PluginDebugCommands.StepOut,
            PluginDebugCommands.Threads,
            PluginDebugCommands.StackTrace,
            PluginDebugCommands.Variables,
            PluginDebugCommands.Completions,
            PluginDebugCommands.SetVariable,
            PluginDebugCommands.Evaluate,
            PluginDebugCommands.SetExpression,
            PluginDebugCommands.Heartbeat,
            PluginDebugCommands.Disconnect
        ];
        return SupportsAssemblyUpload()
            ? [.. commands, PluginDebugCommands.UploadAssembly]
            : commands;
    }

    private bool SupportsAssemblyUpload()
        => session.Options.EvaluatorProvider.TrustProfile != PluginDebugEvaluationTrustProfile.SandboxOnly;

    private byte[] Success(PluginDebugEnvelope request, object body)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(body).Length > session.Options.MaxSnapshotBytes)
        {
            return Error(request, "snapshotTooLarge", "The debug response exceeds the host snapshot limit.");
        }

        try
        {
            return Response(request, request.Kind + "Response", new { success = true, body });
        }
        catch (PluginDebugProtocolException exception) when (exception.Code == "messageTooLarge")
        {
            return Error(request, "snapshotTooLarge", "The debug response exceeds the host message limit.");
        }
    }

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
