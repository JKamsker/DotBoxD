using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedFactoryRegistrySignatureDiagnosticsTests
{
    [Fact]
    public void RegisterServices_IncompatibleFactoryReturn_ThrowsCatalogDiagnostic()
    {
        var assembly = CompileAndLoad(IncompatibleReturnGeneratedFactorySource);
        var sink = new RegistrationSink();

        var thrown = Record.Exception(() => GeneratedServiceRegistry.RegisterServices(new[] { assembly }, sink));

        var ex = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("DotBoxD.Services.Generated.DotBoxDGenerated", ex.Message);
        Assert.Contains("RegisterServices", ex.Message);
        Assert.Contains(assembly.FullName!, ex.Message);
        Assert.Empty(sink.Services);
    }

    [Fact]
    public void RegisterGeneratedServices_IncompatibleFactoryReturn_ThrowsCatalogDiagnostic()
    {
        var assembly = CompileAndLoad(IncompatibleReturnGeneratedFactorySource);
        var sink = new GeneratedRegistrationSink();

        var thrown = Record.Exception(() => GeneratedServiceRegistry.RegisterGeneratedServices(new[] { assembly }, sink));

        var ex = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("DotBoxD.Services.Generated.DotBoxDGenerated", ex.Message);
        Assert.Contains("RegisterGeneratedServices", ex.Message);
        Assert.Contains(assembly.FullName!, ex.Message);
        Assert.Empty(sink.Services);
    }

    private const string IncompatibleReturnGeneratedFactorySource = """
        using System;
        using System.Buffers;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Services.Generated;
        using DotBoxD.Services.Serialization;
        using DotBoxD.Services.Server;
        using DotBoxD.Services.Streaming.Remote;

        namespace SignatureDiagnostics.Sample
        {
            public interface IGreeter
            {
            }

            public sealed class GreeterProxy : IGreeter
            {
            }

            public sealed class GreeterDispatcher : IServiceDispatcher
            {
                public string ServiceName => "IGreeter";

                public Task DispatchAsync(
                    string method,
                    ReadOnlyMemory<byte> payload,
                    ISerializer serializer,
                    IInstanceRegistry registry,
                    IBufferWriter<byte> output,
                    CancellationToken ct = default) =>
                    Task.CompletedTask;
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGenerated
            {
                public static int RegisterServices(IRpcServiceRegistrationSink sink)
                {
                    sink.AddService<
                        global::SignatureDiagnostics.Sample.IGreeter,
                        global::SignatureDiagnostics.Sample.GreeterProxy>();
                    return 1;
                }

                public static int RegisterGeneratedServices(IRpcGeneratedServiceRegistrationSink sink)
                {
                    sink.AddService<
                        global::SignatureDiagnostics.Sample.IGreeter,
                        global::SignatureDiagnostics.Sample.GreeterProxy,
                        global::SignatureDiagnostics.Sample.GreeterDispatcher>();
                    return 1;
                }
            }
        }
        """;
}
