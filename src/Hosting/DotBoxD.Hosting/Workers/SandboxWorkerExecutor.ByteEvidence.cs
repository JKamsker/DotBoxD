using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

internal sealed partial class SandboxWorkerExecutor
{
    private static bool TryRecordBindingEvidence(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        Dictionary<string, int> observedBindingCalls,
        ref long observedBindingBaseFuel,
        ref ObservedBindingBytes observedBytes,
        DateTimeOffset grantClock)
    {
        if (auditEvent.BindingId is null ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var binding))
        {
            return false;
        }

        try
        {
            observedBindingBaseFuel = checked(observedBindingBaseFuel + binding.CostModel.BaseFuel);
        }
        catch (OverflowException)
        {
            return false;
        }

        var calls = observedBindingCalls.TryGetValue(auditEvent.BindingId, out var existing)
            ? existing + 1
            : 1;
        observedBindingCalls[auditEvent.BindingId] = calls;
        return (binding.CostModel.MaxCallsPerRun is not { } maxCalls || calls <= maxCalls) &&
            TryRecordBindingByteEvidence(auditEvent, ref observedBytes) &&
            WorkerFileAuditGrantValidator.Matches(plan, auditEvent, grantClock);
    }

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

    private struct ObservedBindingBytes
    {
        public long FileBytesRead;
        public long FileBytesWritten;
        public long NetworkBytesRead;
        public long NetworkBytesWritten;
    }
}
