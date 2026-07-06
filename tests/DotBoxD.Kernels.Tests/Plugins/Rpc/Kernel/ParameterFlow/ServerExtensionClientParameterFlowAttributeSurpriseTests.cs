using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientParameterFlowAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_parameter_flow_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "EchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(System.Threading.CancellationToken)])!;

        AssertParameterAttribute<AllowNullAttribute>(method, "key");
        AssertParameterAttribute<DisallowNullAttribute>(method, "name");
    }

    [Fact]
    public void Service_backed_receiver_method_extension_preserves_parameter_flow_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "EchoValue([global::System.Diagnostics.CodeAnalysis.AllowNullAttribute] string @key, " +
            "[global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute] string @name, ");
    }

    private static void AssertParameterAttribute<TAttribute>(MethodInfo method, string parameterName)
        where TAttribute : Attribute
    {
        var parameter = Assert.Single(method.GetParameters(), parameter => parameter.Name == parameterName);
        Assert.Single(parameter.GetCustomAttributes<TAttribute>());
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private const string ServiceBackedSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface IEchoService
        {
            ValueTask<string> EchoAsync(
                [AllowNull] string key,
                [DisallowNull] string name,
                CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public string Echo(string key, string name, HookContext ctx) => key + name;
        }
        """;
}
