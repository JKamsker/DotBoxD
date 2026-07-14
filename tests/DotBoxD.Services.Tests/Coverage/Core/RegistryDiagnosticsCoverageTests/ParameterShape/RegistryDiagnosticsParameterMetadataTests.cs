using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RegistryDiagnosticsParameterMetadataTests
{
    [Theory]
    [MemberData(nameof(InvalidParameterMetadataServices))]
    public void RegisterServices_RejectsIncoherentParameterMetadataBeforePublishing(
        string scenario,
        GeneratedService service)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.ParameterMetadata." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.RegisterServices(assembly, new[] { service }));

        Assert.Equal("services", ex.ParamName);
        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
    }

    [Theory]
    [MemberData(nameof(InvalidParameterMetadataServices))]
    public void Register_RejectsIncoherentParameterMetadataBeforeReplacingService(
        string scenario,
        GeneratedService service)
    {
        GeneratedServiceRegistry.Register<IParameterMetadataService>(
            _ => new ParameterMetadataProxy(),
            _ => new ParameterMetadataDispatcher(),
            ParameterMetadataService(MethodWithParameter(ValidCancellationTokenParameter())));

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IParameterMetadataService>(
                _ => new ParameterMetadataProxy(),
                _ => new ParameterMetadataDispatcher(),
                service));

        Assert.Equal("service", ex.ParamName);

        var published = GeneratedServiceRegistry.GetService<IParameterMetadataService>();
        var method = Assert.Single(published.Methods);
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal(typeof(CancellationToken), parameter.Type);
        Assert.True(parameter.IsCancellationToken);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    [Fact]
    public void RegisterServices_AcceptsCoherentParameterMetadata()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.ValidParameterMetadata." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var service = ParameterMetadataService(MethodWithParameters(
            NonDefaultParameter(),
            DefaultValueParameter(),
            ValidCancellationTokenParameter(position: 2)));

        GeneratedServiceRegistry.RegisterServices(assembly, new[] { service });

        var published = Assert.Single(GeneratedServiceRegistry.GetServices(assembly));
        var parameters = Assert.Single(published.Methods).Parameters;
        Assert.Equal(3, parameters.Count);
        Assert.False(parameters[0].HasDefaultValue);
        Assert.Null(parameters[0].DefaultValue);
        Assert.True(parameters[1].HasDefaultValue);
        Assert.Equal("guest", parameters[1].DefaultValue);
        Assert.True(parameters[2].IsCancellationToken);
        Assert.True(parameters[2].HasDefaultValue);
        Assert.Null(parameters[2].DefaultValue);
    }

    public static TheoryData<string, GeneratedService> InvalidParameterMetadataServices => new()
    {
        {
            "StringMarkedAsCancellationToken",
            ParameterMetadataService(MethodWithParameter(
                NonDefaultParameter() with { IsCancellationToken = true }))
        },
        {
            "CancellationTokenFlagMissing",
            ParameterMetadataService(MethodWithParameter(
                ValidCancellationTokenParameter() with { IsCancellationToken = false }))
        },
        {
            "DefaultValueWithoutDefaultFlag",
            ParameterMetadataService(MethodWithParameter(
                NonDefaultParameter() with { DefaultValue = 7 }))
        },
        {
            "CancellationTokenCarriesDefaultValue",
            ParameterMetadataService(MethodWithParameter(
                ValidCancellationTokenParameter() with { DefaultValue = "not generated metadata" }))
        }
    };

    private static GeneratedService ParameterMetadataService(GeneratedMethod method) =>
        new(
            typeof(IParameterMetadataService),
            typeof(ParameterMetadataProxy),
            typeof(ParameterMetadataDispatcher),
            "ParameterMetadata")
        {
            Methods = new[] { method }
        };

    private static GeneratedMethod MethodWithParameter(GeneratedParameter parameter) =>
        MethodWithParameters(parameter);

    private static GeneratedMethod MethodWithParameters(params GeneratedParameter[] parameters) =>
        new(
            "DoAsync",
            "do",
            typeof(Task),
            ResultType: null,
            GeneratedReturnKind.Task,
            ReturnsNestedService: false,
            parameters);

    private static GeneratedParameter NonDefaultParameter(int position = 0) =>
        new(
            "amount",
            typeof(int),
            position,
            IsCancellationToken: false,
            HasDefaultValue: false,
            DefaultValue: null);

    private static GeneratedParameter DefaultValueParameter(int position = 1) =>
        new(
            "label",
            typeof(string),
            position,
            IsCancellationToken: false,
            HasDefaultValue: true,
            DefaultValue: "guest");

    private static GeneratedParameter ValidCancellationTokenParameter(int position = 0) =>
        new(
            "ct",
            typeof(CancellationToken),
            position,
            IsCancellationToken: true,
            HasDefaultValue: true,
            DefaultValue: null);

    public interface IParameterMetadataService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    private sealed class ParameterMetadataProxy : IParameterMetadataService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ParameterMetadataDispatcher : IServiceDispatcher
    {
        public string ServiceName => "ParameterMetadata";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
