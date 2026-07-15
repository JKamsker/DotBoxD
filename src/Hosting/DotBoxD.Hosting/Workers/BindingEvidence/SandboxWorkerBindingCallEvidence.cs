using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting;

internal static class SandboxWorkerBindingCallEvidence
{
    public static bool TryRecord(
        BindingSignature binding,
        string bindingId,
        SandboxWorkerBindingEvidenceRelationship relationship,
        Dictionary<string, int> observedBindingCalls,
        ref long observedBindingBaseFuel)
    {
        if (!TryGetNextCallCount(observedBindingCalls, bindingId, out var calls))
        {
            return false;
        }

        var isTerminalCallLimitAttempt = IsTerminalCallLimitAttempt(binding, relationship, calls);
        if (!CallCountMatches(binding, calls, isTerminalCallLimitAttempt))
        {
            return false;
        }

        observedBindingCalls[bindingId] = calls;
        return isTerminalCallLimitAttempt ||
            TryAddBindingBaseFuel(binding, ref observedBindingBaseFuel);
    }

    private static bool TryGetNextCallCount(
        IReadOnlyDictionary<string, int> observedBindingCalls,
        string bindingId,
        out int calls)
    {
        var existing = observedBindingCalls.TryGetValue(bindingId, out var observed)
            ? observed
            : 0;
        calls = existing == int.MaxValue ? 0 : existing + 1;
        return existing < int.MaxValue;
    }

    private static bool IsTerminalCallLimitAttempt(
        BindingSignature binding,
        SandboxWorkerBindingEvidenceRelationship relationship,
        int calls)
        => relationship == SandboxWorkerBindingEvidenceRelationship.TerminalQuotaFailure &&
           binding.CostModel.MaxCallsPerRun is { } maximum &&
           maximum < int.MaxValue &&
           calls == maximum + 1;

    private static bool CallCountMatches(
        BindingSignature binding,
        int calls,
        bool isTerminalCallLimitAttempt)
        => binding.CostModel.MaxCallsPerRun is not { } maximum ||
           calls <= maximum ||
           isTerminalCallLimitAttempt;

    private static bool TryAddBindingBaseFuel(
        BindingSignature binding,
        ref long observedBindingBaseFuel)
    {
        try
        {
            observedBindingBaseFuel = checked(observedBindingBaseFuel + binding.CostModel.BaseFuel);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
