using Microsoft.CodeAnalysis;

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

    [Fact]
    public void Explicit_inaccessible_referenced_control_service_reports_focused_diagnostic_without_generated_accessibility_leak()
    {
        var reference = PluginServerGenerationTestDriver.CompileReference(
            ReferencedControlServiceSource,
            "ReferencedControlServiceContract");
        var inputDiagnostics = PluginServerGenerationTestDriver.InputDiagnostics(
            ReferencedInaccessibleControlServiceSource,
            reference);
        var (_, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics(
                ReferencedInaccessibleControlServiceSource,
                reference);
        var outputDiagnostics = outputCompilation.GetDiagnostics();

        var inputAccessDiagnostics = inputDiagnostics
            .Where(diagnostic => diagnostic.Id == "CS0122")
            .Where(diagnostic => diagnostic.ToString().Contains("IInternalControlService", StringComparison.Ordinal))
            .ToArray();
        var controlServiceDiagnostics = generatorDiagnostics
            .Where(diagnostic => diagnostic.Id == "DBXK100")
            .Where(
                diagnostic =>
                {
                    var message = diagnostic.GetMessage();
                    return message.Contains("IInternalControlService", StringComparison.Ordinal) &&
                           (message.Contains("inaccessible", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("cannot be named", StringComparison.OrdinalIgnoreCase));
                })
            .ToArray();
        var generatedControlLeaks = outputDiagnostics
            .Where(IsGeneratedDiagnostic)
            .Where(diagnostic => diagnostic.Id is "CS0122" or "CS0234")
            .Where(diagnostic => diagnostic.ToString().Contains("IInternalControlService", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            inputAccessDiagnostics.Length > 0 &&
            controlServiceDiagnostics.Length > 0 &&
            generatedControlLeaks.Length == 0,
            $"""
            Expected input CS0122, DBXK100 for the inaccessible referenced control service, and no generated accessibility leak.

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
            [RpcService]
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

    private const string ReferencedControlServiceSource = """
        using System.Threading;
        using System.Threading.Tasks;

        namespace External.Control
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            internal interface IInternalControlService : DotBoxD.Plugins.IServerExtensionWireClient
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
        """;

    private const string ReferencedInaccessibleControlServiceSource = """
        using DotBoxD.Services.Attributes;
        using External.Control;

        namespace Regression.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
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
                ControlService = typeof(IInternalControlService))]
            internal partial class RemotePluginServer : IGameWorldAccess;

            internal sealed partial class RemotePluginContext;
        }
        """;

    private static bool IsGeneratedDiagnostic(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree?.ToString().StartsWith("// <auto-generated/>", StringComparison.Ordinal) == true;

    private static string Format(IEnumerable<object> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
