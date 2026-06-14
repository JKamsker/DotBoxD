namespace SafeIR;

public sealed partial class SandboxContext
{
    internal bool CanBulkChargeBindingCalls(BindingDescriptor descriptor, long calls)
    {
        if (calls < 0 ||
            descriptor.RequiredCapability is not null ||
            descriptor.CostModel.MaxCallsPerRun is not null ||
            AllowedBindingIds is not null && !AllowedBindingIds.Contains(descriptor.Id) ||
            !Budget.CanChargeHostCalls(calls))
        {
            return false;
        }

        try
        {
            return Budget.CanChargeFuel(CheckedFuel(calls, descriptor.CostModel.BaseFuel));
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal void ChargeBindingCalls(BindingDescriptor descriptor, long calls)
    {
        if (calls == 0)
        {
            return;
        }

        if (!CanBulkChargeBindingCalls(descriptor, calls))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                $"binding call budget exhausted at {descriptor.Id}"));
        }

        Budget.ChargeHostCalls(descriptor.Id, calls);
        ChargeFuel(CheckedFuel(calls, descriptor.CostModel.BaseFuel));
    }
}
