using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RegistryDiagnosticsReturnMetadataTests
{
    [Theory]
    [MemberData(nameof(InvalidReturnMetadataServices))]
    public void RegisterServices_RejectsIncoherentReturnMetadataBeforePublishing(
        string scenario,
        GeneratedService service)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.ReturnMetadata." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.RegisterServices(assembly, new[] { service }));

        Assert.Equal("services", ex.ParamName);
        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
    }

    [Theory]
    [MemberData(nameof(InvalidReturnMetadataServices))]
    public void Register_RejectsIncoherentReturnMetadataBeforeReplacingService(
        string scenario,
        GeneratedService service)
    {
        GeneratedServiceRegistry.Register<IReturnMetadataService>(
            _ => new ReturnMetadataProxy(),
            _ => new ReturnMetadataDispatcher(),
            ReturnMetadataService(StringTaskMethod(typeof(string))));

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IReturnMetadataService>(
                _ => new ReturnMetadataProxy(),
                _ => new ReturnMetadataDispatcher(),
                service));

        Assert.Equal("service", ex.ParamName);

        var published = GeneratedServiceRegistry.GetService<IReturnMetadataService>();
        var method = Assert.Single(published.Methods);
        Assert.Equal(typeof(string), method.ResultType);
        Assert.False(method.ReturnsNestedService);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    [Theory]
    [MemberData(nameof(ValidReturnMetadataServices))]
    public void RegisterServices_AcceptsCoherentReturnMetadata(
        string scenario,
        GeneratedService service)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.ValidReturnMetadata." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        GeneratedServiceRegistry.RegisterServices(assembly, new[] { service });

        var published = Assert.Single(GeneratedServiceRegistry.GetServices(assembly));
        var method = Assert.Single(published.Methods);
        Assert.NotNull(method.ResultType);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    public static TheoryData<string, GeneratedService> InvalidReturnMetadataServices => new()
    {
        { "TaskOfTMissingResultType", ReturnMetadataService(StringTaskMethod(resultType: null)) },
        { "ValueTaskOfTMissingResultType", ReturnMetadataService(ValueTaskMethod(resultType: null)) },
        { "TaskOfStreamMissingResultType", ReturnMetadataService(StreamTaskMethod(resultType: null)) },
        { "NestedTaskMissingFlag", ReturnMetadataService(NestedTaskMethod(returnsNestedService: false)) },
        { "NonNestedTaskHasFlag", ReturnMetadataService(StringTaskMethod(typeof(string), returnsNestedService: true)) }
    };

    public static TheoryData<string, GeneratedService> ValidReturnMetadataServices => new()
    {
        { "TaskOfTHasResultType", ReturnMetadataService(StringTaskMethod(typeof(string))) },
        { "NestedTaskHasFlag", ReturnMetadataService(NestedTaskMethod(returnsNestedService: true)) }
    };

    private static GeneratedService ReturnMetadataService(GeneratedMethod method) =>
        new(
            typeof(IReturnMetadataService),
            typeof(ReturnMetadataProxy),
            typeof(ReturnMetadataDispatcher),
            "ReturnMetadata")
        {
            Methods = new[] { method }
        };

    private static GeneratedMethod StringTaskMethod(Type? resultType, bool returnsNestedService = false) =>
        Method(
            "GetNameAsync",
            "getName",
            typeof(Task<string>),
            resultType,
            GeneratedReturnKind.TaskOfT,
            returnsNestedService);

    private static GeneratedMethod ValueTaskMethod(Type? resultType) =>
        Method(
            "GetValueAsync",
            "getValue",
            typeof(ValueTask<int>),
            resultType,
            GeneratedReturnKind.ValueTaskOfT,
            returnsNestedService: false);

    private static GeneratedMethod StreamTaskMethod(Type? resultType) =>
        Method(
            "OpenAsync",
            "open",
            typeof(Task<Stream>),
            resultType,
            GeneratedReturnKind.TaskOfStream,
            returnsNestedService: false);

    private static GeneratedMethod NestedTaskMethod(bool returnsNestedService) =>
        Method(
            "GetNestedAsync",
            "getNested",
            typeof(Task<INestedReturnMetadataService>),
            typeof(INestedReturnMetadataService),
            GeneratedReturnKind.TaskOfNestedService,
            returnsNestedService);

    private static GeneratedMethod Method(
        string name,
        string wireName,
        Type returnType,
        Type? resultType,
        GeneratedReturnKind returnKind,
        bool returnsNestedService) =>
        new(name, wireName, returnType, resultType, returnKind, returnsNestedService, Array.Empty<GeneratedParameter>());

    public interface IReturnMetadataService
    {
        Task<string> GetNameAsync();
    }

    public interface INestedReturnMetadataService
    {
        Task PingAsync();
    }

    private sealed class ReturnMetadataProxy : IReturnMetadataService
    {
        public Task<string> GetNameAsync() => Task.FromResult("ok");
    }

    private sealed class ReturnMetadataDispatcher : IServiceDispatcher
    {
        public string ServiceName => "ReturnMetadata";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
