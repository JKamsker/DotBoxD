using System.Reflection;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class GeneratedServiceRegistryNullAssemblyCoverageTests
{
    [Fact]
    public void RegisterServices_NullAssemblyElement_ReportsAssembliesParameter()
    {
        var sink = new RecordingServiceSink();
        var assemblies = new Assembly[] { null! };

        var thrown = Record.Exception(() =>
        {
            GeneratedServiceRegistry.RegisterServices(assemblies, sink);
        });
        var ex = Assert.IsAssignableFrom<ArgumentException>(thrown);

        Assert.Equal("assemblies", ex.ParamName);
        Assert.Empty(sink.ServiceTypes);
    }

    [Fact]
    public void RegisterGeneratedServices_NullAssemblyElement_ReportsAssembliesParameter()
    {
        var sink = new RecordingGeneratedSink();
        var assemblies = new Assembly[] { null! };

        var thrown = Record.Exception(() =>
        {
            GeneratedServiceRegistry.RegisterGeneratedServices(assemblies, sink);
        });
        var ex = Assert.IsAssignableFrom<ArgumentException>(thrown);

        Assert.Equal("assemblies", ex.ParamName);
        Assert.Empty(sink.ServiceTypes);
    }
}
