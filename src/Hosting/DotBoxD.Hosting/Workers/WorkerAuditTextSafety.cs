using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Hosting;

internal static class WorkerAuditTextSafety
{
    public static bool TextIsSafe(string? value)
        => value is null ||
           string.Equals(AuditTextSanitizer.SanitizeAndRedact(value), value, StringComparison.Ordinal);
}
