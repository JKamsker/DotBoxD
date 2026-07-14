using System.Text.Json;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class PluginDebugAssemblyUploadTests
{
    [Fact]
    public async Task Trusted_profile_negotiates_chunked_upload_and_detach_discards_session_state()
    {
        using var server = PluginServer.Create(remoteDebugOptions: Options(new TrustedEvaluator()));
        using var owner = server.CreateSession();
        await using var session = owner.CreateDebugSession(new NoopEvents());

        var initialized = await SuccessAsync(session, PluginDebugCommands.Initialize);
        Assert.Contains(
            PluginDebugCommands.UploadAssembly,
            initialized.GetProperty("commands").EnumerateArray().Select(item => item.GetString()));
        Assert.True(initialized.GetProperty("evaluator").GetProperty("supportsAssemblyUpload").GetBoolean());
        _ = await SuccessAsync(session, PluginDebugCommands.Attach);

        var first = await SuccessAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 0, [1, 2], complete: false));
        Assert.Equal(2, first.GetProperty("received").GetInt32());
        _ = await SuccessAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 2, [3, 4], complete: true));

        _ = await SuccessAsync(session, PluginDebugCommands.Disconnect);
        _ = await SuccessAsync(session, PluginDebugCommands.Attach);
        var replacement = await SuccessAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 0, [9], complete: true));
        Assert.Equal(1, replacement.GetProperty("received").GetInt32());
    }

    [Fact]
    public async Task Sandbox_only_profile_neither_negotiates_nor_accepts_assembly_upload()
    {
        using var server = PluginServer.Create(remoteDebugOptions: Options(SandboxOnlyPluginDebugEvaluator.Instance));
        using var owner = server.CreateSession();
        await using var session = owner.CreateDebugSession(new NoopEvents());

        var initialized = await SuccessAsync(session, PluginDebugCommands.Initialize);
        Assert.DoesNotContain(
            PluginDebugCommands.UploadAssembly,
            initialized.GetProperty("commands").EnumerateArray().Select(item => item.GetString()));
        Assert.False(initialized.GetProperty("evaluator").GetProperty("supportsAssemblyUpload").GetBoolean());
        var rejected = await ExchangeAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 0, [1], complete: true));

        Assert.False(rejected.GetProperty("success").GetBoolean());
        Assert.Equal("assemblyUploadDenied", rejected.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Upload_enforces_total_limit_and_exact_offsets()
    {
        using var server = PluginServer.Create(remoteDebugOptions: Options(new TrustedEvaluator(), maxUploadBytes: 4));
        using var owner = server.CreateSession();
        await using var session = owner.CreateDebugSession(new NoopEvents());
        _ = await SuccessAsync(session, PluginDebugCommands.Attach);
        _ = await SuccessAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 0, [1, 2, 3], complete: false));

        var wrongOffset = await ExchangeAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 1, [4], complete: true));
        Assert.Equal("assemblyUploadRejected", wrongOffset.GetProperty("error").GetProperty("code").GetString());

        var oversized = await ExchangeAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk("Plugin.dll", offset: 3, [4, 5], complete: true));
        Assert.Equal("assemblyUploadRejected", oversized.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("../Plugin.dll")]
    [InlineData("..\\Plugin.dll")]
    public async Task Upload_rejects_non_leaf_assembly_names_on_every_platform(string fileName)
    {
        using var server = PluginServer.Create(remoteDebugOptions: Options(new TrustedEvaluator()));
        using var owner = server.CreateSession();
        await using var session = owner.CreateDebugSession(new NoopEvents());
        _ = await SuccessAsync(session, PluginDebugCommands.Attach);

        var rejected = await ExchangeAsync(
            session,
            PluginDebugCommands.UploadAssembly,
            Chunk(fileName, offset: 0, [1], complete: true));

        Assert.Equal("assemblyUploadRejected", rejected.GetProperty("error").GetProperty("code").GetString());
    }

    private static PluginRemoteDebugOptions Options(
        IPluginDebugEvaluatorProvider evaluator,
        int maxUploadBytes = 1024) =>
        new()
        {
            Enabled = true,
            EvaluatorProvider = evaluator,
            MaxAssemblyUploadBytes = maxUploadBytes
        };

    private static object Chunk(string fileName, int offset, byte[] content, bool complete) => new
    {
        fileName,
        offset,
        content = Convert.ToBase64String(content),
        complete
    };

    private static async Task<JsonElement> SuccessAsync(
        PluginDebugSession session,
        string command,
        object? payload = null)
    {
        var response = await ExchangeAsync(session, command, payload);
        Assert.True(response.GetProperty("success").GetBoolean(), response.ToString());
        return response.GetProperty("body");
    }

    private static async Task<JsonElement> ExchangeAsync(
        PluginDebugSession session,
        string command,
        object? payload = null)
    {
        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            session.SessionToken,
            JsonSerializer.SerializeToElement(payload ?? new { }));
        var response = await session.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024));
        return PluginDebugProtocol.Decode(response, 1024 * 1024).Payload;
    }

    private sealed class TrustedEvaluator : IPluginDebugEvaluatorProvider
    {
        public string Id => "test-trusted";

        public PluginDebugEvaluationTrustProfile TrustProfile => PluginDebugEvaluationTrustProfile.TrustedWorker;

        public bool SupportsAwait => true;

        public ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
            PluginDebugEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PluginDebugEvaluationResult.Success(SandboxValue.Unit));
    }

    private sealed class NoopEvents : IPluginDebugEventEndpoint
    {
        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
