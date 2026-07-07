using System.Reflection;
using System.Reflection.Emit;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.RegistryDiagnosticsTestSupport;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RegistryDiagnosticsRegistrationTests
{
    [Fact]
    public void Register_MetadataDescribingWrongServiceType_ThrowsArgumentException()
    {
        // ValidateService must reject metadata whose ServiceType disagrees with TService.
        var mismatched = new GeneratedService(
            typeof(IGameService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                mismatched));

        Assert.Contains("registered for", ex.Message);
    }

    [Fact]
    public void Register_MetadataMissingServiceName_ThrowsArgumentException()
    {
        var noName = new GeneratedService(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            string.Empty);

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                noName));

        Assert.Contains("service name", ex.Message);
    }

    [Fact]
    public void Register_WithValidMetadata_MakesServiceResolvable()
    {
        // A full round-trip: register a hand-authored service interface, then resolve its proxy,
        // dispatcher, and metadata through the public API.
        GeneratedServiceRegistry.Register<ICustomRegisteredService>(
            _ => new CustomProxy(),
            _ => new CustomDispatcher(),
            ValidCustomService());

        var metadata = GeneratedServiceRegistry.GetService<ICustomRegisteredService>();
        var proxy = GeneratedServiceRegistry.CreateProxy<ICustomRegisteredService>(new RecordingInvoker());
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ICustomRegisteredService>(new CustomImplementation());

        Assert.Equal("Custom", metadata.ServiceName);
        Assert.IsType<CustomProxy>(proxy);
        Assert.Equal("Custom", dispatcher.ServiceName);
    }

    [Fact]
    public void Register_DuplicateRegistration_ReplacesPreviousFactory()
    {
        GeneratedServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV1(),
            _ => new CustomDispatcher(),
            new GeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV1),
                typeof(CustomDispatcher),
                "Replaceable"));

        GeneratedServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV2(),
            _ => new CustomDispatcher(),
            new GeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV2),
                typeof(CustomDispatcher),
                "Replaceable"));

        var proxy = GeneratedServiceRegistry.CreateProxy<IReplaceableService>(new RecordingInvoker());

        // The latest registration wins.
        Assert.IsType<ReplaceableProxyV2>(proxy);
    }

    [Fact]
    public void RegisterServices_SnapshotsCallerOwnedCatalogLists()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.MutableCatalog." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var parameters = new List<GeneratedParameter>
        {
            new("ct", typeof(CancellationToken), 0, IsCancellationToken: true, HasDefaultValue: true, DefaultValue: null)
        };
        var methods = new List<GeneratedMethod>
        {
            new(
                "DoAsync",
                "do",
                typeof(Task),
                ResultType: null,
                GeneratedReturnKind.Task,
                ReturnsNestedService: false,
                parameters)
        };
        var services = new List<GeneratedService>
        {
            ValidCustomService() with { Methods = methods }
        };

        GeneratedServiceRegistry.RegisterServices(assembly, services);
        services.Clear();
        methods.Clear();
        parameters.Clear();

        var published = GeneratedServiceRegistry.GetServices(assembly);
        var service = Assert.Single(published);
        var method = Assert.Single(service.Methods);
        var parameter = Assert.Single(method.Parameters);

        Assert.NotSame(services, published);
        Assert.NotSame(methods, service.Methods);
        Assert.NotSame(parameters, method.Parameters);
        Assert.Equal("Custom", service.ServiceName);
        Assert.Equal("DoAsync", method.Name);
        Assert.Equal("ct", parameter.Name);
    }

}
