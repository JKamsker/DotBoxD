namespace SafeIR;

public sealed partial class ResourceMeter
{
    public void ChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        if (iterations < 0) { throw new ArgumentOutOfRangeException(nameof(iterations)); }
        if (fuelPerIteration <= 0) { throw new ArgumentOutOfRangeException(nameof(fuelPerIteration)); }
        if (iterations == 0)
        {
            return;
        }

        LoopIterations = AddChecked(LoopIterations, iterations, "loop iteration budget exhausted");
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }

        ChargeFuel(MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted"));
    }

    internal bool CanChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        if (iterations < 0 || fuelPerIteration <= 0)
        {
            return false;
        }

        try
        {
            return LoopIterations <= Limits.MaxLoopIterations - iterations &&
                   CanChargeFuel(MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted"));
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal bool CanChargeFuel(long amount)
        => amount >= 0 && FuelUsed <= Limits.MaxFuel - amount;

    internal bool CanChargeHostCalls(long calls)
        => calls >= 0 &&
           calls <= int.MaxValue &&
           HostCalls <= Limits.MaxHostCalls - calls;

    internal void ChargeHostCalls(string bindingId, long calls)
    {
        if (!CanChargeHostCalls(calls))
        {
            throw Quota($"host call budget exhausted at {bindingId}");
        }

        var count = checked((int)calls);
        HostCalls = AddChecked(HostCalls, count, $"host call budget exhausted at {bindingId}");
        var bindingCalls = _callsByBinding.TryGetValue(bindingId, out var existing)
            ? AddChecked(existing, count, $"binding call budget exhausted at {bindingId}")
            : count;
        _callsByBinding[bindingId] = bindingCalls;
    }
}
