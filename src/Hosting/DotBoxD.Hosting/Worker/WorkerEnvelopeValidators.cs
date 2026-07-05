using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Worker;

internal static class WorkerEnvelopeValidators
{
    private const int MaxSafeMessageLength = 1_024;
    private const int MaxDiagnosticIdLength = 128;

    private static readonly BudgetField[] BudgetFields =
    [
        new("maxFuel", static budget => budget.MaxFuel),
        new("maxLoopIterations", static budget => budget.MaxLoopIterations),
        new("maxAllocatedBytes", static budget => budget.MaxAllocatedBytes),
        new("maxHostCalls", static budget => budget.MaxHostCalls),
        new("maxFileBytesRead", static budget => budget.MaxFileBytesRead),
        new("maxFileBytesWritten", static budget => budget.MaxFileBytesWritten),
        new("maxNetworkBytesRead", static budget => budget.MaxNetworkBytesRead),
        new("maxNetworkBytesWritten", static budget => budget.MaxNetworkBytesWritten),
        new("maxLogEvents", static budget => budget.MaxLogEvents),
        new("maxCollectionElements", static budget => budget.MaxTotalCollectionElements),
        new("maxStringBytes", static budget => budget.MaxTotalStringBytes),
    ];

    private static readonly string[] SecretMarkers =
    [
        "authorization",
        "bearer",
        "password",
        "passwd",
        "secret",
        "token",
        "api-key",
        "apikey",
        "client-key",
        "client_key",
        "client-secret",
        "client_secret",
        "private-key",
        "private_key"
    ];

    internal static bool ErrorMatches(SandboxError? error)
        => error is not null &&
           Enum.IsDefined(error.Code) &&
           IsSafeText(error.SafeMessage, MaxSafeMessageLength, allowColon: true) &&
           IsSafeOptionalDiagnosticId(error.DiagnosticId);

    internal static bool BudgetFieldsMatch(ExecutionPlan plan, SandboxAuditEvent summary)
    {
        foreach (var field in BudgetFields)
        {
            if (!FieldEquals(summary, field.Key, field.Value(plan.Budget)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeOptionalDiagnosticId(string? value)
        => value is null || IsSafeText(value, MaxDiagnosticIdLength, allowColon: false);

    private static bool IsSafeText(string? value, int maxLength, bool allowColon)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || ContainsSecretMarker(value))
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c) || (!allowColon && c == ':'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FieldEquals(SandboxAuditEvent summary, string key, long value)
        => summary.Fields is not null &&
           summary.Fields.TryGetValue(key, out var actual) &&
           string.Equals(
               actual,
               value.ToString(System.Globalization.CultureInfo.InvariantCulture),
               StringComparison.Ordinal);

    private static bool ContainsSecretMarker(string value)
    {
        var normalized = value.ToLowerInvariant();
        foreach (var marker in SecretMarkers)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct BudgetField(string Key, Func<ResourceLimits, long> Value);
}
