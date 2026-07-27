using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RegistryDiagnosticsDuplicateServiceNameTests
{
    [Fact]
    public void RegisterServices_RejectsDuplicateServiceNamesBeforePublishing()
    {
        var assembly = CreateCatalogAssembly(nameof(RegisterServices_RejectsDuplicateServiceNamesBeforePublishing));
        var services = new[]
        {
            FirstService() with { ServiceName = "same" },
            SecondService() with { ServiceName = "same" }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.RegisterServices(assembly, services));

        Assert.Equal("services", ex.ParamName);
        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void RegisterServices_AcceptsDistinctServiceNames()
    {
        var assembly = CreateCatalogAssembly(nameof(RegisterServices_AcceptsDistinctServiceNames));

        GeneratedServiceRegistry.RegisterServices(assembly, new[] { FirstService(), SecondService() });

        var services = GeneratedServiceRegistry.GetServices(assembly);
        Assert.Equal(new[] { "first", "second" }, services.Select(service => service.ServiceName).ToArray());
    }

    private static Assembly CreateCatalogAssembly(string scenario) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(
                "DotBoxD.Services.Tests.DuplicateServiceNames." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

    private static GeneratedService FirstService() =>
        new(
            typeof(IDuplicateServiceNameFirstService),
            typeof(DuplicateServiceNameFirstProxy),
            typeof(DuplicateServiceNameFirstDispatcher),
            "first",
            new[] { FirstMethod() });

    private static GeneratedService SecondService() =>
        new(
            typeof(IDuplicateServiceNameSecondService),
            typeof(DuplicateServiceNameSecondProxy),
            typeof(DuplicateServiceNameSecondDispatcher),
            "second",
            new[] { SecondMethod() });

    private static GeneratedMethod FirstMethod() =>
        new(
            "ReadAsync",
            "read",
            typeof(Task<string>),
            ResultType: typeof(string),
            GeneratedReturnKind.TaskOfT,
            ReturnsNestedService: false,
            Array.Empty<GeneratedParameter>());

    private static GeneratedMethod SecondMethod() =>
        new(
            "WriteAsync",
            "write",
            typeof(Task),
            ResultType: null,
            GeneratedReturnKind.Task,
            ReturnsNestedService: false,
            Array.Empty<GeneratedParameter>());

    private interface IDuplicateServiceNameFirstService
    {
        Task<string> ReadAsync();
    }

    private interface IDuplicateServiceNameSecondService
    {
        Task WriteAsync();
    }

    private sealed class DuplicateServiceNameFirstProxy : IDuplicateServiceNameFirstService
    {
        public Task<string> ReadAsync() => Task.FromResult("value");
    }

    private sealed class DuplicateServiceNameSecondProxy : IDuplicateServiceNameSecondService
    {
        public Task WriteAsync() => Task.CompletedTask;
    }

    private sealed class DuplicateServiceNameFirstDispatcher : IServiceDispatcher
    {
        public string ServiceName => "first";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DuplicateServiceNameSecondDispatcher : IServiceDispatcher
    {
        public string ServiceName => "second";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
