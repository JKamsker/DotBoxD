using System.Text.Json;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginDebugSessionTests
{
    [Fact]
    public async Task Disabled_server_reports_protocol_support_without_allowing_attach()
    {
        using var server = PluginServer.Create();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(new RecordingEvents());

        var initialized = await ExchangeAsync(debug, PluginDebugCommands.Initialize);
        var attached = await ExchangeAsync(debug, PluginDebugCommands.Attach);

        Assert.True(initialized.GetProperty("success").GetBoolean());
        Assert.False(initialized.GetProperty("body").GetProperty("supported").GetBoolean());
        Assert.Contains(
            PluginDebugCommands.Completions,
            initialized.GetProperty("body").GetProperty("commands").EnumerateArray()
                .Select(command => command.GetString()));
        Assert.Equal("debuggingDisabled", ErrorCode(attached));
    }

    [Fact]
    public async Task Session_token_cannot_authorize_another_plugin_session()
    {
        using var server = EnabledServer();
        using var firstOwner = server.CreateSession();
        using var secondOwner = server.CreateSession();
        await using var first = firstOwner.CreateDebugSession(new RecordingEvents());
        await using var second = secondOwner.CreateDebugSession(new RecordingEvents());
        var request = Request(PluginDebugCommands.Initialize, first.SessionToken);

        var error = await Assert.ThrowsAsync<PluginDebugProtocolException>(
            async () => await second.ExchangeAsync(request));

        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task Unsupported_protocol_version_returns_a_version_one_error()
    {
        using var server = EnabledServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(new RecordingEvents());
        var request = PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                99,
                PluginDebugCommands.Initialize,
                "request-1",
                debug.SessionToken,
                JsonSerializer.SerializeToElement(new { })),
            1024 * 1024);

        var response = PluginDebugProtocol.Decode(await debug.ExchangeAsync(request), 1024 * 1024);

        Assert.Equal(PluginDebugProtocol.Version, response.Version);
        Assert.Equal("unsupportedVersion", ErrorCode(response.Payload));
    }

    [Fact]
    public async Task Only_one_debugger_can_attach_and_disconnect_releases_the_slot()
    {
        using var server = EnabledServer();
        using var firstOwner = server.CreateSession();
        using var secondOwner = server.CreateSession();
        await using var first = firstOwner.CreateDebugSession(new RecordingEvents());
        await using var second = secondOwner.CreateDebugSession(new RecordingEvents());

        Assert.True((await ExchangeAsync(first, PluginDebugCommands.Attach)).GetProperty("success").GetBoolean());
        Assert.Equal(
            "debuggerAlreadyAttached",
            ErrorCode(await ExchangeAsync(second, PluginDebugCommands.Attach)));

        _ = await ExchangeAsync(first, PluginDebugCommands.Disconnect);

        Assert.True((await ExchangeAsync(second, PluginDebugCommands.Attach)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Attached_session_rejects_a_second_attach_request()
    {
        using var server = EnabledServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(new RecordingEvents());

        Assert.True((await ExchangeAsync(debug, PluginDebugCommands.Attach)).GetProperty("success").GetBoolean());

        var duplicate = await ExchangeAsync(debug, PluginDebugCommands.Attach);

        Assert.Equal("debuggerAlreadyAttached", ErrorCode(duplicate));
        Assert.True(debug.IsAttached);
    }

    [Fact]
    public async Task Host_restricted_pause_scope_is_rejected()
    {
        using var server = EnabledServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(new RecordingEvents());

        var response = await ExchangeAsync(
            debug,
            PluginDebugCommands.Attach,
            new { pauseScope = "execution" });

        Assert.Equal("pauseScopeDenied", ErrorCode(response));
        Assert.False(debug.IsAttached);
    }

    [Fact]
    public async Task Lease_expiry_detaches_and_releases_the_server_slot()
    {
        var options = EnabledOptions() with { StopLease = TimeSpan.FromMilliseconds(40) };
        using var server = PluginServer.Create(remoteDebugOptions: options);
        using var firstOwner = server.CreateSession();
        using var secondOwner = server.CreateSession();
        await using var first = firstOwner.CreateDebugSession(new RecordingEvents());
        await using var second = secondOwner.CreateDebugSession(new RecordingEvents());
        _ = await ExchangeAsync(first, PluginDebugCommands.Attach);

        await WaitUntilAsync(() => !first.IsAttached);

        Assert.True((await ExchangeAsync(second, PluginDebugCommands.Attach)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Disposing_plugin_owner_detaches_its_debugger()
    {
        using var server = EnabledServer();
        var firstOwner = server.CreateSession();
        using var secondOwner = server.CreateSession();
        var first = firstOwner.CreateDebugSession(new RecordingEvents());
        await using var second = secondOwner.CreateDebugSession(new RecordingEvents());
        _ = await ExchangeAsync(first, PluginDebugCommands.Attach);

        firstOwner.Dispose();

        Assert.False(first.IsAttached);
        Assert.True((await ExchangeAsync(second, PluginDebugCommands.Attach)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Plugin_session_allows_only_one_debug_endpoint()
    {
        using var server = EnabledServer();
        using var owner = server.CreateSession();
        using var debug = owner.CreateDebugSession(new RecordingEvents());

        Assert.Throws<InvalidOperationException>(() => owner.CreateDebugSession(new RecordingEvents()));
    }

    [Fact]
    public async Task Debugger_attach_and_kernel_registration_do_not_deadlock()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            using var server = EnabledServer();
            using var owner = server.CreateSession();
            await using var debug = owner.CreateDebugSession(new RecordingEvents());
            var attach = ExchangeAsync(debug, PluginDebugCommands.Attach);
            var install = owner.InstallAsync(FireDamagePluginPackage.Create()).AsTask();

            await Task.WhenAll(attach, install).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static PluginServer EnabledServer()
        => PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            remoteDebugOptions: EnabledOptions());

    private static PluginRemoteDebugOptions EnabledOptions()
        => new()
        {
            Enabled = true,
            AllowedPauseScopes = [KernelDebugPauseScope.Server]
        };

    private static async Task<JsonElement> ExchangeAsync(
        PluginDebugSession session,
        string command,
        object? payload = null)
    {
        var response = await session.ExchangeAsync(Request(command, session.SessionToken, payload));
        return PluginDebugProtocol.Decode(response, 1024 * 1024).Payload;
    }

    private static byte[] Request(string command, string token, object? payload = null)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                command,
                Guid.NewGuid().ToString("N"),
                token,
                JsonSerializer.SerializeToElement(payload ?? new { })),
            1024 * 1024);

    private static string? ErrorCode(JsonElement payload)
        => payload.GetProperty("error").GetProperty("code").GetString();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingEvents : IPluginDebugEventEndpoint
    {
        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
