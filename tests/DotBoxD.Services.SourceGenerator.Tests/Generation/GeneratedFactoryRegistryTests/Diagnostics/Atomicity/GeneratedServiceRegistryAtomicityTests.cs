using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedServiceRegistryAtomicityTests
{
    [Fact]
    public void RegisterServices_MalformedLaterAssembly_DoesNotPartiallyRegisterServices()
    {
        var validAssembly = CompileAndLoad(ValidServiceFactorySource);
        var malformedAssembly = CompileAndLoad(IncompatibleReturnGeneratedFactorySource);
        var sink = new RegistrationSink();

        var thrown = Record.Exception(
            () => GeneratedServiceRegistry.RegisterServices(new[] { validAssembly, malformedAssembly }, sink));

        var exception = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("RegisterServices", exception.Message);
        Assert.Contains(malformedAssembly.FullName!, exception.Message);
        Assert.Empty(sink.Services);
    }

    [Fact]
    public void RegisterGeneratedServices_MalformedLaterAssembly_DoesNotPartiallyRegisterServices()
    {
        var validAssembly = CompileAndLoad(ValidServiceFactorySource);
        var malformedAssembly = CompileAndLoad(IncompatibleReturnGeneratedFactorySource);
        var sink = new GeneratedRegistrationSink();

        var thrown = Record.Exception(
            () => GeneratedServiceRegistry.RegisterGeneratedServices(new[] { validAssembly, malformedAssembly }, sink));

        var exception = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("RegisterGeneratedServices", exception.Message);
        Assert.Contains(malformedAssembly.FullName!, exception.Message);
        Assert.Empty(sink.Services);
    }

    [Fact]
    public void RegisterGeneratedServices_ValidAssemblies_RegistersEveryService()
    {
        var firstAssembly = CompileAndLoad(ValidServiceFactorySource);
        var secondAssembly = CompileAndLoad(SecondValidServiceFactorySource);
        var sink = new GeneratedRegistrationSink();

        GeneratedServiceRegistry.RegisterGeneratedServices(new[] { firstAssembly, secondAssembly }, sink);

        Assert.Equal(2, sink.Services.Count);
    }

    private const string ValidServiceFactorySource = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Atomicity.Valid
        {
            [RpcService]
            public interface IGreeter
            {
                Task PingAsync();
            }
        }
        """;

    private const string SecondValidServiceFactorySource = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Atomicity.SecondValid
        {
            [RpcService]
            public interface IGreeter
            {
                Task PingAsync();
            }
        }
        """;

    private const string IncompatibleReturnGeneratedFactorySource = """
        using System;
        using System.Buffers;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Services.Generated;
        using DotBoxD.Services.Serialization;
        using DotBoxD.Services.Server;

        namespace Atomicity.Malformed
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
                        global::Atomicity.Malformed.IGreeter,
                        global::Atomicity.Malformed.GreeterProxy>();
                    return 1;
                }

                public static int RegisterGeneratedServices(IRpcGeneratedServiceRegistrationSink sink)
                {
                    sink.AddService<
                        global::Atomicity.Malformed.IGreeter,
                        global::Atomicity.Malformed.GreeterProxy,
                        global::Atomicity.Malformed.GreeterDispatcher>();
                    return 1;
                }
            }
        }
        """;
}
