using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotBoxD.Codecs.MessagePack;

internal static class ConstructorReplayValidatorAdmission
{
    internal const int SuccessfulReplayThreshold = 8192;
    internal const int CreationStartedState = 1;
    private const int UnavailableState = 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryClaimCreation(ref int successfulReplays, ref int creationStarted)
    {
        if (creationStarted != 0)
        {
            return false;
        }

        // Admission is intentionally approximate. Racing writes can only lose progress and postpone
        // this optional optimization; correctness remains in the reflective validation performed first.
        if (successfulReplays < 0)
        {
            return false;
        }

        if (successfulReplays < ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold)
        {
            successfulReplays++;
            if (successfulReplays < ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold)
            {
                return false;
            }
        }

        return Interlocked.CompareExchange(ref creationStarted, CreationStartedState, 0) == 0;
    }

    public static void Publish<T>(
        ref int successfulReplays,
        ref int creationState,
        ConstructorInfo constructor,
        PropertyInfo[] parameterProperties,
        PropertyInfo[] boundProperties)
    {
        var validator = ConstructorReplayValidatorCompiler.TryCreate(
            constructor,
            parameterProperties,
            boundProperties);
        if (validator is null)
        {
            Volatile.Write(ref successfulReplays, int.MinValue);
            Volatile.Write(ref creationState, UnavailableState);
            return;
        }

        Volatile.Write(ref ConstructorReplayValidatorStorage<T>.Validator, validator);
    }
}

internal static class ConstructorReplayValidatorStorage<T>
{
    // Kept in a separate generic static so cold/AOT types never allocate GC-tracked validator storage.
    public static Func<object, bool>? Validator;
}
