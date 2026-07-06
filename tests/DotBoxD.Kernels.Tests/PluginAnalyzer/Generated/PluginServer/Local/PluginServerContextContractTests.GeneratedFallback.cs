namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    [Fact]
    public async Task Local_context_member_used_in_unresolved_generated_remote_stage_reports_DBXK116()
    {
        var diagnostics = await AnalyzerDiagnosticsAsync(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                [NativeOnly]
                public string NativeName => "local";
            }

            public sealed record Ping(string Id);

            public static class Usage
            {
                public static void Configure(RemotePluginServer server)
                    => server.Hooks.On<Ping>()
                        .Where((Ping e, GameContext ctx) => ctx.NativeName == "local")
                        .Run((Ping e, GameContext ctx) => { });
            }
            """));

        Assert.Contains(diagnostics, d => d.Id == "DBXK116");
    }
}
