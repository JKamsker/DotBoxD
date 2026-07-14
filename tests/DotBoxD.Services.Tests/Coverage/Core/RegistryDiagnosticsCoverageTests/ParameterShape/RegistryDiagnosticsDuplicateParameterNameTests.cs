using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RegistryDiagnosticsDuplicateParameterNameTests
{
    [Fact]
    public void Register_RejectsDuplicateParameterNamesBeforeReplacingService()
    {
        GeneratedServiceRegistry.Register<IDuplicateParameterRegistrationService>(
            _ => new DuplicateParameterRegistrationProxy(),
            _ => new DuplicateParameterRegistrationDispatcher(),
            ValidRegistrationService());

        var invalid = ValidRegistrationService() with
        {
            Methods = new[] { MethodWithDuplicateParameterNames() }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IDuplicateParameterRegistrationService>(
                _ => new DuplicateParameterRegistrationProxy(),
                _ => new DuplicateParameterRegistrationDispatcher(),
                invalid));

        Assert.Equal("service", ex.ParamName);

        var published = GeneratedServiceRegistry.GetService<IDuplicateParameterRegistrationService>();
        var method = Assert.Single(published.Methods);
        Assert.Equal(new[] { "id", "value" }, method.Parameters.Select(parameter => parameter.Name).ToArray());
    }

    [Fact]
    public void RegisterServices_RejectsDuplicateParameterNamesBeforePublishing()
    {
        var assembly = CreateCatalogAssembly(nameof(RegisterServices_RejectsDuplicateParameterNamesBeforePublishing));
        var invalid = ValidCatalogService() with
        {
            Methods = new[] { MethodWithDuplicateParameterNames() }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.RegisterServices(assembly, new[] { invalid }));

        Assert.Equal("services", ex.ParamName);
        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void RegisterServices_AcceptsDistinctParameterNames()
    {
        var assembly = CreateCatalogAssembly(nameof(RegisterServices_AcceptsDistinctParameterNames));

        GeneratedServiceRegistry.RegisterServices(assembly, new[] { ValidCatalogService() });

        var service = Assert.Single(GeneratedServiceRegistry.GetServices(assembly));
        var method = Assert.Single(service.Methods);
        Assert.Equal(new[] { "id", "value" }, method.Parameters.Select(parameter => parameter.Name).ToArray());
    }

    private static Assembly CreateCatalogAssembly(string scenario) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(
                "DotBoxD.Services.Tests.DuplicateParameterNames." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

    private static GeneratedService ValidRegistrationService() =>
        new(
            typeof(IDuplicateParameterRegistrationService),
            typeof(DuplicateParameterRegistrationProxy),
            typeof(DuplicateParameterRegistrationDispatcher),
            "DuplicateParameterRegistration",
            new[] { MethodWithDistinctParameterNames() });

    private static GeneratedService ValidCatalogService() =>
        new(
            typeof(IDuplicateParameterCatalogService),
            typeof(DuplicateParameterCatalogProxy),
            typeof(DuplicateParameterCatalogDispatcher),
            "DuplicateParameterCatalog",
            new[] { MethodWithDistinctParameterNames() });

    private static GeneratedMethod MethodWithDistinctParameterNames() =>
        ValidMethod(new[]
        {
            new GeneratedParameter(
                "id",
                typeof(string),
                Position: 0,
                IsCancellationToken: false,
                HasDefaultValue: false,
                DefaultValue: null),
            new GeneratedParameter(
                "value",
                typeof(string),
                Position: 1,
                IsCancellationToken: false,
                HasDefaultValue: false,
                DefaultValue: null)
        });

    private static GeneratedMethod MethodWithDuplicateParameterNames() =>
        ValidMethod(new[]
        {
            new GeneratedParameter(
                "id",
                typeof(string),
                Position: 0,
                IsCancellationToken: false,
                HasDefaultValue: false,
                DefaultValue: null),
            new GeneratedParameter(
                "id",
                typeof(string),
                Position: 1,
                IsCancellationToken: false,
                HasDefaultValue: false,
                DefaultValue: null)
        });

    private static GeneratedMethod ValidMethod(IReadOnlyList<GeneratedParameter> parameters) =>
        new(
            "UpdateAsync",
            "update",
            typeof(Task),
            ResultType: null,
            GeneratedReturnKind.Task,
            ReturnsNestedService: false,
            parameters);

    private interface IDuplicateParameterRegistrationService
    {
        Task UpdateAsync(string id, string value);
    }

    private interface IDuplicateParameterCatalogService
    {
        Task UpdateAsync(string id, string value);
    }

    private sealed class DuplicateParameterRegistrationProxy : IDuplicateParameterRegistrationService
    {
        public Task UpdateAsync(string id, string value) => Task.CompletedTask;
    }

    private sealed class DuplicateParameterCatalogProxy : IDuplicateParameterCatalogService
    {
        public Task UpdateAsync(string id, string value) => Task.CompletedTask;
    }

    private sealed class DuplicateParameterRegistrationDispatcher : IServiceDispatcher
    {
        public string ServiceName => "DuplicateParameterRegistration";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DuplicateParameterCatalogDispatcher : IServiceDispatcher
    {
        public string ServiceName => "DuplicateParameterCatalog";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
