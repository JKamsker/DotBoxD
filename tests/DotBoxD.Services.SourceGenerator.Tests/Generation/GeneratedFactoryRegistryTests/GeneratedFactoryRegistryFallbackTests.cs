using System.Reflection;
using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedFactoryRegistryFallbackTests
{
    [Fact]
    public void Registry_ReportsClearDiagnosticWhenGeneratorDidNotRun()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedServiceRegistry.CreateProxy(typeof(INotGeneratedService), new NullClient()));

        Assert.Contains("No DotBoxD generated factory is registered", ex.Message);
        Assert.Contains("[RpcService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    [Fact]
    public void Registry_ReturnsEmptyServiceCatalogWhenAssemblyHasNoGeneratedRegistry()
    {
        var assembly = typeof(GeneratedFactoryRegistryTests).Assembly;

        var services = GeneratedServiceRegistry.GetServices(assembly);

        Assert.Empty(services);
        Assert.Same(services, GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void Registry_ReadsLegacyGeneratedServicesCatalog()
    {
        const string source = """
            using System.Collections.Generic;
            using DotBoxD.Services.Generated;

            namespace Legacy.Sample
            {
                public interface ILegacyService
                {
                }

                public sealed class LegacyServiceProxy : ILegacyService
                {
                }

                public sealed class LegacyServiceDispatcher
                {
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGenerated
                {
                    private static readonly GeneratedService[] s_services =
                    {
                        new GeneratedService(
                            typeof(global::Legacy.Sample.ILegacyService),
                            typeof(global::Legacy.Sample.LegacyServiceProxy),
                            typeof(global::Legacy.Sample.LegacyServiceDispatcher),
                            "ILegacyService"),
                    };

                    public static IReadOnlyList<GeneratedService> Services => s_services;
                }
            }
            """;

        var assembly = CompileAndLoad(source);

        var services = GeneratedServiceRegistry.GetServices(assembly);

        var service = Assert.Single(services);
        Assert.Equal("ILegacyService", service.ServiceName);
        Assert.Equal("LegacyServiceProxy", service.ProxyType.Name);
        Assert.Same(services, GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void Registry_WrapsThrowingLegacyGeneratedServicesGetterWithCatalogContext()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using DotBoxD.Services.Generated;

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGenerated
                {
                    public static IReadOnlyList<GeneratedService> Services =>
                        throw new InvalidOperationException("legacy Services getter failed");
                }
            }
            """;

        var assembly = CompileAndLoad(source);

        var thrown = Record.Exception(() => GeneratedServiceRegistry.GetServices(assembly));

        Assert.IsNotType<TargetInvocationException>(thrown);
        var ex = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("DotBoxD.Services.Generated.DotBoxDGenerated", ex.Message);
        Assert.Contains(assembly.FullName!, ex.Message);
        Assert.Contains("Services", ex.Message);

        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("legacy Services getter failed", inner.Message);
    }
}
