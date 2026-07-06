using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

internal static class ResourceMeterMath
{
    public static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));

    public static void ThrowIfNegative(long amount, string paramName)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    public static long AddChecked(long current, long amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

    public static long AddNonNegativeChecked(long current, long amount, string quotaMessage)
    {
        var result = unchecked(current + amount);
        return result < current ? throw Quota(quotaMessage) : result;
    }

    public static int AddChecked(int current, int amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

    public static long MultiplyChecked(long left, long right, string quotaMessage)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }
}
