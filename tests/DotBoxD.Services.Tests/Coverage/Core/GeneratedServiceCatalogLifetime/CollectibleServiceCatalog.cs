using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Coverage.Core;

public static class CollectibleServiceCatalog
{
    public static Fixture Create(bool generated)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"CollectibleCatalog_{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Catalog");
        var service = module.DefineType("IService", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        var proxyBuilder = module.DefineType("Proxy", TypeAttributes.Public | TypeAttributes.Sealed);
        proxyBuilder.AddInterfaceImplementation(service);
        proxyBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        var proxy = proxyBuilder.CreateType()!;
        var metadata = new GeneratedService(service, proxy, typeof(Dispatcher), "Collectible");
        if (generated)
        {
            var builder = module.DefineType("DotBoxD.Services.Generated.DotBoxDGenerated",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var values = builder.DefineField("Catalog", typeof(IReadOnlyList<GeneratedService>), FieldAttributes.Public | FieldAttributes.Static);
            var getter = builder.DefineMethod("get_Services",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(IReadOnlyList<GeneratedService>), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, values);
            il.Emit(OpCodes.Ret);
            builder.DefineProperty("Services", PropertyAttributes.None, typeof(IReadOnlyList<GeneratedService>), null).SetGetMethod(getter);
            DefineSink(builder, "RegisterServices", typeof(IRpcServiceRegistrationSink), [service, proxy]);
            DefineSink(builder, "RegisterGeneratedServices", typeof(IRpcGeneratedServiceRegistrationSink), [service, proxy, typeof(Dispatcher)]);
            builder.CreateType()!.GetField("Catalog")!.SetValue(null, new[] { metadata });
        }

        return new Fixture(service, proxy, metadata);
    }

    private static void DefineSink(TypeBuilder builder, string name, Type sink, Type[] arguments)
    {
        var method = builder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), [sink]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, sink.GetMethod("AddService")!.MakeGenericMethod(arguments));
        il.Emit(OpCodes.Ret);
    }

    public sealed record Fixture(Type Service, Type Proxy, GeneratedService Metadata)
    {
        public Assembly Assembly => Service.Assembly;
    }

    public sealed class Dispatcher : IServiceDispatcher
    {
        public string ServiceName => "Collectible";

        public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default) => Task.CompletedTask;
    }
}
