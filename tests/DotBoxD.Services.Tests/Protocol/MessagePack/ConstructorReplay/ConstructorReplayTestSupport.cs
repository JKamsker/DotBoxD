using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

internal static class ConstructorReplayTestSupport
{
    public const int AllocationIterations = 1000;

    public static ArrayBufferWriter<byte> Warm<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        serializer.Serialize(writer, value);
        writer.Clear();
        return writer;
    }

    public static long MeasureAllocated<T>(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> writer,
        T value)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < AllocationIterations; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public static ConstructorReplayGuard GetGuard(Type runtimeType)
    {
        var guardsField = typeof(ConstructorReplayGuard).GetField(
            "Guards",
            BindingFlags.NonPublic | BindingFlags.Static);
        var guards = (ConditionalWeakTable<Type, ConstructorReplayGuard>?)guardsField?.GetValue(null)
            ?? throw new InvalidOperationException("Constructor replay guard storage was not found.");
        return guards.TryGetValue(runtimeType, out var guard)
            ? guard
            : throw new InvalidOperationException("The payload type has no cached constructor replay guard.");
    }

    public static Func<object, bool>? GetValidator(Type runtimeType)
    {
        var field = typeof(ConstructorReplayGuard).GetField(
            "_validator",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Constructor replay validator field was not found.");
        return (Func<object, bool>?)field.GetValue(GetGuard(runtimeType));
    }
}
