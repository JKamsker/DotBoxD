using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackConstructorReplayPublicationTests
{
    [Fact]
    public void Published_validator_survives_a_lagging_admission_count_write()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new PublicationRaceDto(42);
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        writer.Clear();

        var guard = GetGuard(typeof(PublicationRaceDto));
        var successfulReplays = GetGuardField("_successfulReplays");
        var creationState = GetGuardField("_validatorCreationState");
        successfulReplays.SetValue(
            guard,
            ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold - 1);
        creationState.SetValue(guard, ConstructorReplayValidatorAdmission.CreationStartedState);

        var validatorCalls = 0;
        Volatile.Write(
            ref ConstructorReplayValidatorStorage<PublicationRaceDto>.Validator,
            _ =>
            {
                validatorCalls++;
                return true;
            });
        try
        {
            serializer.Serialize(writer, value);
            Assert.Equal(1, validatorCalls);
        }
        finally
        {
            Volatile.Write(ref ConstructorReplayValidatorStorage<PublicationRaceDto>.Validator, null);
            successfulReplays.SetValue(guard, 0);
            creationState.SetValue(guard, 0);
        }
    }

    private static ConstructorReplayGuard GetGuard(Type type)
    {
        var guardsField = typeof(ConstructorReplayGuard).GetField(
            "Guards",
            BindingFlags.NonPublic | BindingFlags.Static);
        var guards = Assert.IsAssignableFrom<ConcurrentDictionary<Type, ConstructorReplayGuard>>(
            guardsField?.GetValue(null));
        return guards[type];
    }

    private static FieldInfo GetGuardField(string name) =>
        Assert.IsAssignableFrom<FieldInfo>(typeof(ConstructorReplayGuard).GetField(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance));

    public sealed class PublicationRaceDto
    {
        public PublicationRaceDto(int id) => Id = id;

        public int Id { get; }
    }
}
