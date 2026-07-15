using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientResponseLanguageVersionTests
{
    [Fact]
    public void Async_direct_client_with_synchronous_response_helper_compiles_as_CSharp12()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            AsyncDirectClientSource,
            LanguageVersion.CSharp12);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private const string AsyncDirectClientSource = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "async-direct")]
        public sealed partial class AsyncKernel
        {
        #pragma warning disable CS1998
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public async ValueTask<int> ReadAsync(HookContext ctx)
            {
                return 1;
            }
        #pragma warning restore CS1998
        }

        public static class Probe
        {
            public static ValueTask<int> ReadAsync(RemoteControl control) => control.ReadAsync();
        }
        """;
}
