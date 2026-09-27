using System.Reflection;
using System.Reflection.Emit;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public static class CollectibleReplayPayload
{
    public static Type CreateType(bool guarded)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ReplayPayload_{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var parent = guarded ? typeof(Value) : typeof(object);
        var builder = assembly.DefineDynamicModule("Payload").DefineType("Payload", TypeAttributes.Public | TypeAttributes.Sealed, parent);
        if (guarded)
        {
            var constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]);
            constructor.DefineParameter(1, ParameterAttributes.None, "id");
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, parent.GetConstructor([typeof(int)])!);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            builder.DefineDefaultConstructor(MethodAttributes.Public);
        }

        return builder.CreateType()!;
    }

    public class Value(int id)
    {
        public int Id { get; } = id;
    }

    // An explicit formatter isolates our constructor cache from MessagePack's dynamic formatter caches.
    // A generic formatter also covers exact collectible declarations without storing Type keys itself.
    internal sealed class Resolver : IFormatterResolver
    {
        public static readonly Resolver Instance = new();

        public IMessagePackFormatter<T> GetFormatter<T>() => NilFormatter<T>.Instance;
    }

    private sealed class NilFormatter<T> : IMessagePackFormatter<T>
    {
        public static readonly NilFormatter<T> Instance = new();

        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options) => writer.WriteNil();

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            reader.ReadNil();
            return default!;
        }
    }
}
