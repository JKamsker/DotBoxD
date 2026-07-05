namespace DotBoxD.Kernels.Sandbox;

internal static class SandboxCounterGuards
{
    internal static long RequireNonNegative(long value, string paramName, string message)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        return value;
    }

    internal static int RequireNonNegative(int value, string paramName, string message)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        return value;
    }

    internal static long? RequireNonNegative(long? value, string paramName, string message)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        return value;
    }
}
