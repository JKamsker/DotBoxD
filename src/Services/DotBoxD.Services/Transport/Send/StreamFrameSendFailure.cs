using System.Runtime.ExceptionServices;

namespace DotBoxD.Services.Transport;

/// <summary>Creates completed owned-frame send failures with their original exception shape.</summary>
internal static class StreamFrameSendFailure
{
    public static ValueTask Create(Exception error)
    {
        if (error is OperationCanceledException canceled)
        {
            return CreateCanceledAsync(canceled);
        }

        return new ValueTask(Task.FromException(error));
    }

    private static async ValueTask CreateCanceledAsync(OperationCanceledException error)
    {
        await default(ValueTask).ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(error).Throw();
    }
}
