using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionAttributeNullMetadataTests
{
    [Fact]
    public void Server_extension_with_explicit_null_id_reports_diagnostic()
    {
        var diagnostics = Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtension(null, typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        AssertServerExtensionDiagnostic(diagnostics, "id");
    }

    [Fact]
    public void Server_extension_with_explicit_null_service_type_reports_diagnostic()
    {
        var diagnostics = Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            [ServerExtension("echo", null)]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        AssertServerExtensionDiagnostic(diagnostics, "serviceType");
    }

    [Fact]
    public void Graft_server_extension_with_explicit_null_id_does_not_report_diagnostic()
    {
        var diagnostics = Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            public sealed class EchoGraft
            {
                public int Echo(int value, HookContext ctx) => value;
            }

            [ServerExtension(typeof(EchoGraft), null)]
            public sealed partial class EchoKernel
            {
            }
            """);

        AssertNoServerExtensionDiagnostic(diagnostics);
    }

    [Fact]
    public void Graft_server_extension_with_named_null_id_does_not_report_diagnostic()
    {
        var diagnostics = Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;

            namespace Sample;

            public sealed class EchoGraft
            {
                public int Echo(int value, HookContext ctx) => value;
            }

            [ServerExtension(typeof(EchoGraft), id: null)]
            public sealed partial class EchoKernel
            {
            }
            """);

        AssertNoServerExtensionDiagnostic(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Diagnostics(string source)
        => PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

    private static void AssertServerExtensionDiagnostic(
        IReadOnlyList<Diagnostic> diagnostics,
        string parameterName)
    {
        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ServerExtension", StringComparison.Ordinal) &&
                 d.GetMessage().Contains(parameterName, StringComparison.Ordinal));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    private static void AssertNoServerExtensionDiagnostic(IReadOnlyList<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ServerExtension", StringComparison.Ordinal));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
