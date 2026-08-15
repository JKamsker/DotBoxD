using System.Reflection;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class GeneratedServiceCatalogPublicationRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task GetServices_ConcurrentExplicitRegistration_RemainsAuthoritative()
    {
        var legacyServices = new[] { GeneratedServiceRegistry.GetService<IPlayerNotifications>() };
        var explicitServices = new[] { GeneratedServiceRegistry.GetService<IGameService>() };
        var gate = BlockingLegacyCatalog.Configure(legacyServices);
        var assembly = CreateLegacyCatalogAssembly();

        var loading = Task.Run(() => GeneratedServiceRegistry.GetServices(assembly));
        try
        {
            await gate.GetterEntered.Task.WaitAsync(Timeout);
            GeneratedServiceRegistry.RegisterServices(assembly, explicitServices);
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }

        var inFlightServices = await loading.WaitAsync(Timeout);
        var laterServices = GeneratedServiceRegistry.GetServices(assembly);

        Assert.Single(inFlightServices);
        Assert.Equal(typeof(IGameService), inFlightServices[0].ServiceType);
        Assert.Same(inFlightServices, laterServices);
        Assert.Equal(typeof(IGameService), laterServices[0].ServiceType);
    }

    private static Assembly CreateLegacyCatalogAssembly()
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("GeneratedServiceCatalogRace_" + Guid.NewGuid().ToString("N")),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Catalog");
        var generatedType = module.DefineType(
            "DotBoxD.Services.Generated.DotBoxDGenerated",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var getter = generatedType.DefineMethod(
            "get_Services",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(IReadOnlyList<DotBoxD.Services.Generated.GeneratedService>),
            Type.EmptyTypes);
        getter.GetILGenerator().Emit(
            System.Reflection.Emit.OpCodes.Call,
            typeof(BlockingLegacyCatalog).GetMethod(nameof(BlockingLegacyCatalog.GetServices))!);
        getter.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);

        generatedType
            .DefineProperty("Services", PropertyAttributes.None, typeof(IReadOnlyList<DotBoxD.Services.Generated.GeneratedService>), null)
            .SetGetMethod(getter);
        _ = generatedType.CreateType();

        return assembly;
    }

    public static class BlockingLegacyCatalog
    {
        private static LegacyCatalogGate? s_gate;

        public static LegacyCatalogGate Configure(IReadOnlyList<DotBoxD.Services.Generated.GeneratedService> services)
        {
            var gate = new LegacyCatalogGate(services);
            Volatile.Write(ref s_gate, gate);
            return gate;
        }

        public static IReadOnlyList<DotBoxD.Services.Generated.GeneratedService> GetServices()
        {
            var gate = Volatile.Read(ref s_gate) ?? throw new InvalidOperationException("Legacy catalog gate was not configured.");
            gate.GetterEntered.TrySetResult(true);
            gate.Release.Task.GetAwaiter().GetResult();
            return gate.Services;
        }
    }

    public sealed class LegacyCatalogGate(IReadOnlyList<DotBoxD.Services.Generated.GeneratedService> services)
    {
        public TaskCompletionSource<bool> GetterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<DotBoxD.Services.Generated.GeneratedService> Services { get; } = services;
    }
}
