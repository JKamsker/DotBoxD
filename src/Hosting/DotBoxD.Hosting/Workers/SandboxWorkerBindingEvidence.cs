using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

internal static class SandboxWorkerBindingEvidence
{
    internal static bool TryRecordBindingEvidence(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        SandboxWorkerBindingEvidenceRelationship relationship,
        Dictionary<string, int> observedBindingCalls,
        ref long observedBindingBaseFuel,
        ref ObservedBindingBytes observedBytes,
        ref DeterministicRandomAuditSequence deterministicRandom,
        DateTimeOffset grantClock,
        out bool representsCall)
    {
        representsCall = relationship !=
            SandboxWorkerBindingEvidenceRelationship.TerminalQuotaFailureAfterSuccess;
        if (auditEvent.BindingId is null ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var binding))
        {
            return false;
        }

        if (!representsCall)
        {
            return AdditionalEvidenceMatches(
                plan,
                auditEvent,
                ref observedBytes,
                ref deterministicRandom,
                grantClock);
        }

        if (!SandboxWorkerBindingCallEvidence.TryRecord(
                binding,
                auditEvent.BindingId,
                relationship,
                observedBindingCalls,
                ref observedBindingBaseFuel))
        {
            return false;
        }

        return AdditionalEvidenceMatches(
            plan,
            auditEvent,
            ref observedBytes,
            ref deterministicRandom,
            grantClock);
    }

    private static bool AdditionalEvidenceMatches(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        ref ObservedBindingBytes observedBytes,
        ref DeterministicRandomAuditSequence deterministicRandom,
        DateTimeOffset grantClock)
        => TryRecordBindingByteEvidence(auditEvent, ref observedBytes) &&
            deterministicRandom.Matches(auditEvent) &&
            WorkerFileAuditGrantValidator.Matches(plan, auditEvent, grantClock);

    private static bool TryRecordBindingByteEvidence(
        SandboxAuditEvent auditEvent,
        ref ObservedBindingBytes observedBytes)
        => TryRecordBindingByteEvidenceCore(auditEvent, ref observedBytes);

    private static bool TryRecordBindingByteEvidenceCore(
        SandboxAuditEvent auditEvent,
        ref ObservedBindingBytes observedBytes)
    {
        if (!TryGetByteField(auditEvent, "bytesRead", out var bytesRead) ||
            !TryGetByteField(auditEvent, "bytesWritten", out var bytesWritten) ||
            !ByteSlotMatchesFields(auditEvent, bytesRead, bytesWritten))
        {
            return false;
        }

        if (auditEvent.Bytes is { } bytes && bytesRead is null && bytesWritten is null)
        {
            if (OnlyHasEffect(auditEvent.Effect, SandboxEffect.FileWrite))
            {
                bytesWritten = bytes;
            }
            else
            {
                bytesRead = bytes;
            }
        }

        return TryAddObservedBytes(
                auditEvent.Effect,
                bytesRead,
                SandboxEffect.FileRead,
                ref observedBytes.FileBytesRead,
                ref observedBytes.NetworkBytesRead) &&
            TryAddObservedBytes(
                auditEvent.Effect,
                bytesWritten,
                SandboxEffect.FileWrite,
                ref observedBytes.FileBytesWritten,
                ref observedBytes.NetworkBytesWritten);
    }

    private static bool TryGetByteField(SandboxAuditEvent auditEvent, string key, out long? bytes)
    {
        bytes = null;
        if (auditEvent.Fields is null || !auditEvent.Fields.TryGetValue(key, out var value))
        {
            return true;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 0)
        {
            return false;
        }

        bytes = parsed;
        return true;
    }

    private static bool ByteSlotMatchesFields(SandboxAuditEvent auditEvent, long? bytesRead, long? bytesWritten)
    {
        if (auditEvent.Bytes is not { } bytes)
        {
            return true;
        }

        if (bytesRead is { } read)
        {
            return bytes == read;
        }

        return bytesWritten is not { } written || bytes == written;
    }

    private static bool TryAddObservedBytes(
        SandboxEffect effect,
        long? bytes,
        SandboxEffect fileEffect,
        ref long fileTotal,
        ref long networkTotal)
    {
        if (bytes is null)
        {
            return true;
        }

        var hasFileEffect = (effect & fileEffect) != SandboxEffect.None;
        var hasNetworkEffect = (effect & SandboxEffect.Network) != SandboxEffect.None;
        if (hasFileEffect == hasNetworkEffect)
        {
            return false;
        }

        try
        {
            if (hasFileEffect)
            {
                fileTotal = checked(fileTotal + bytes.Value);
            }
            else
            {
                networkTotal = checked(networkTotal + bytes.Value);
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private static bool OnlyHasEffect(SandboxEffect effect, SandboxEffect expected)
        => (effect & expected) != SandboxEffect.None &&
           (effect & (SandboxEffect.FileRead | SandboxEffect.FileWrite | SandboxEffect.Network)) == expected;

    internal struct ObservedBindingBytes
    {
        public long FileBytesRead;
        public long FileBytesWritten;
        public long NetworkBytesRead;
        public long NetworkBytesWritten;
    }

    internal struct DeterministicRandomAuditSequence
    {
        private SandboxContext? _context;

        public static DeterministicRandomAuditSequence Create(ExecutionPlan plan)
            => plan.Policy is { Deterministic: true, RandomSeed: not null }
                ? new DeterministicRandomAuditSequence
                {
                    _context = new SandboxContext(
                        SandboxRunId.New(),
                        plan.Policy,
                        new ResourceMeter(plan.Budget),
                        new BindingRegistry([]),
                        new InMemoryAuditSink(),
                        CancellationToken.None)
                }
                : default;

        public bool Matches(SandboxAuditEvent auditEvent)
        {
            if (_context is null ||
                !auditEvent.Success ||
                !string.Equals(auditEvent.BindingId, "random.nextI32", StringComparison.Ordinal))
            {
                return true;
            }

            if (auditEvent.Fields is null ||
                !TryGetInt32Field(auditEvent.Fields, "minInclusive", out var minInclusive) ||
                !TryGetInt32Field(auditEvent.Fields, "maxExclusive", out var maxExclusive) ||
                !TryGetInt32Field(auditEvent.Fields, "value", out var value) ||
                minInclusive >= maxExclusive)
            {
                return false;
            }

            try
            {
                return value == _context.NextRandomInt32(minInclusive, maxExclusive);
            }
            catch (SandboxRuntimeException)
            {
                return false;
            }
        }

        private static bool TryGetInt32Field(IReadOnlyDictionary<string, string> fields, string key, out int value)
        {
            value = 0;
            return fields.TryGetValue(key, out var text) &&
                int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }
    }
}
