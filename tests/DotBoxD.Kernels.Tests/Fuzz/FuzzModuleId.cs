using System.Globalization;

namespace DotBoxD.Kernels.Tests.Fuzz;

internal static class FuzzModuleId
{
    public static string FromSeed(string prefix, int seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var signed = (long)seed;
        var magnitude = Math.Abs(signed).ToString(CultureInfo.InvariantCulture);
        var separatedDigits = string.Join("-", magnitude.ToCharArray());
        var sign = signed < 0 ? "n" : "p";
        return $"{prefix}-{sign}{separatedDigits}";
    }
}
