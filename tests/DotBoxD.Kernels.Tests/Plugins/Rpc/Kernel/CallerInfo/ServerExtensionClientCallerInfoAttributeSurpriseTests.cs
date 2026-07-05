using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientCallerInfoAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_caller_info_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.TraceKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "TraceAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(int), typeof(string), typeof(string), typeof(int), typeof(string)])!;

        var parameters = method.GetParameters();
        Assert.Single(parameters[1].GetCustomAttributes<CallerMemberNameAttribute>());
        Assert.Single(parameters[2].GetCustomAttributes<CallerFilePathAttribute>());
        Assert.Single(parameters[3].GetCustomAttributes<CallerLineNumberAttribute>());

        var expressionAttribute = Assert.Single(
            parameters[4].GetCustomAttributes<CallerArgumentExpressionAttribute>());
        Assert.Equal("value", expressionAttribute.ParameterName);
    }

    [Fact]
    public void Service_backed_receiver_extension_preserves_caller_info_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "TraceKernelServerExtensionClientExtensions",
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] string @member = \"\"");
        AssertGeneratedSourceContains(
            generatedSources,
            "TraceKernelServerExtensionClientExtensions",
            "[global::System.Runtime.CompilerServices.CallerFilePathAttribute] string @file = \"\"");
        AssertGeneratedSourceContains(
            generatedSources,
            "TraceKernelServerExtensionClientExtensions",
            "[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] int @line = 0");
        AssertGeneratedSourceContains(
            generatedSources,
            "TraceKernelServerExtensionClientExtensions",
            "[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"value\")] string @expression = \"\"");
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
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface ITraceService
        {
            ValueTask<string> TraceAsync(
                int value,
                [CallerMemberName] string member = "",
                [CallerFilePath] string file = "",
                [CallerLineNumber] int line = 0,
                [CallerArgumentExpression("value")] string expression = "");
        }

        [ServerExtensionClient(typeof(IRemoteControl), "TraceClient")]
        [ServerExtension("trace", typeof(ITraceService))]
        public sealed partial class TraceKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "Trace")]
            public string Trace(
                int value,
                string member,
                string file,
                int line,
                string expression,
                HookContext ctx)
                => member;
        }
        """;
}
