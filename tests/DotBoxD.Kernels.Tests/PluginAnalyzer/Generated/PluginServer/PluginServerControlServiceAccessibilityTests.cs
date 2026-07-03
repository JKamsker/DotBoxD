namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerControlServiceAccessibilityTests
{
    [Fact]
    public void Explicit_file_local_control_service_reports_focused_diagnostic_without_generated_cs0234()
    {
        var inputDiagnostics = PluginServerGenerationTestDriver.InputDiagnostics(Source);
        var (_, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics(Source);
        var outputDiagnostics = outputCompilation.GetDiagnostics();

        var controlServiceDiagnostics = generatorDiagnostics
            .Where(diagnostic => diagnostic.Id == "DBXK100")
            .Where(
                diagnostic =>
                {
                    var message = diagnostic.GetMessage();
                    return message.Contains("IHiddenPluginControlService", StringComparison.Ordinal) &&
                           (message.Contains("file-local", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("inaccessible", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("cannot be named", StringComparison.OrdinalIgnoreCase));
                })
            .ToArray();
        var generatedControlLeaks = outputDiagnostics
            .Where(diagnostic => diagnostic.Id == "CS0234")
            .Where(diagnostic => diagnostic.ToString().Contains("IHiddenPluginControlService", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            inputDiagnostics.Count == 0 &&
            controlServiceDiagnostics.Length > 0 &&
            generatedControlLeaks.Length == 0,
            $"""
            Expected clean input, DBXK100 for the file-local explicit control service, and no generated CS0234 leak.

            Input diagnostics:
            {Format(inputDiagnostics)}

            Generator diagnostics:
            {Format(generatorDiagnostics)}

            Output diagnostics:
            {Format(outputDiagnostics)}
            """);
    }

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Services.Attributes;
        using Regression.Control;

        namespace Regression.Game
        {
            [DotBoxDService]
            public interface IGameWorldAccess;
        }

        namespace Regression.Control
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            file interface IHiddenPluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                    DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace Regression.Plugin
        {
            using Regression.Game;

            [DotBoxD.Abstractions.GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(IHiddenPluginControlService))]
            internal partial class RemotePluginServer : IGameWorldAccess;

            internal sealed partial class RemotePluginContext;
        }
        """;

    private static string Format(IEnumerable<object> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
