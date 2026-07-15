using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Model;

using static ResourceMeterMath;

public sealed class ResourceMeter
{
    private const int FuelDeadlineCheckInterval = 64;
    private const int LoopDeadlineCheckInterval = 4096;

    private ResourceHostCallTracker? _hostCallTracker;
    private long _allocatedBytes;
    private long _fileBytesRead;
    private long _fileBytesWritten;
    private long _networkBytesRead;
    private long _networkBytesWritten;
    private long _deadline;
    private int _chargesSinceDeadlineCheck;

    public ResourceMeter(ResourceLimits limits)
    {
        ResourceLimitValidation.Validate(limits);
        Limits = limits;
        _deadline = ResourceMeterDeadline.Create(limits);
    }

    public ResourceLimits Limits { get; }
    public long FuelUsed { get; private set; }
    public long LoopIterations { get; private set; }
    public long AllocatedBytes => _allocatedBytes;
    public int HostCalls { get; private set; }
    public long FileBytesRead => _fileBytesRead;
    public long FileBytesWritten => _fileBytesWritten;
    public long NetworkBytesRead => _networkBytesRead;
    public long NetworkBytesWritten => _networkBytesWritten;
    public int LogEvents { get; private set; }
    public long CollectionElements { get; private set; }
    public long StringBytes { get; private set; }

    public SandboxResourceUsage Snapshot()
        => ResourceMeterSnapshot.Create(this);

    internal void ResetForReuse()
    {
        _hostCallTracker?.Reset();
        _deadline = ResourceMeterDeadline.Create(Limits);
        _chargesSinceDeadlineCheck = 0;
        FuelUsed = 0;
        LoopIterations = 0;
        _allocatedBytes = 0;
        HostCalls = 0;
        _fileBytesRead = 0;
        _fileBytesWritten = 0;
        _networkBytesRead = 0;
        _networkBytesWritten = 0;
        LogEvents = 0;
        CollectionElements = 0;
        StringBytes = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeFuel(long amount)
        => ChargeFuel(amount, FuelDeadlineCheckInterval);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ChargeFuel(long amount, int deadlineCheckInterval)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        FuelUsed = AddNonNegativeChecked(FuelUsed, amount, "fuel exhausted");
        if (FuelUsed > Limits.MaxFuel)
        {
            throw Quota("fuel exhausted");
        }

        if (++_chargesSinceDeadlineCheck >= deadlineCheckInterval)
        {
            _chargesSinceDeadlineCheck = 0;
            CheckDeadline();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeLoopIteration(long fuelAmount)
    {
        LoopIterations = ResourceMeterUsageCharges.AddSingleLoopIteration(LoopIterations, fuelAmount);
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }

        ChargeFuel(fuelAmount, LoopDeadlineCheckInterval);
    }

    public void ChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        var charge = ResourceMeterUsageCharges.ChargeLoopIterations(LoopIterations, Limits, iterations, fuelPerIteration);
        if (charge.Fuel == 0)
        {
            return;
        }

        LoopIterations = charge.LoopIterations;
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }

        ChargeFuel(charge.Fuel);
    }

    internal bool CanChargeLoopIterations(long iterations, long fuelPerIteration)
        => ResourceMeterUsageCharges.CanChargeLoopIterations(
            LoopIterations,
            FuelUsed,
            Limits,
            iterations,
            fuelPerIteration);

    internal bool CanChargeFuel(long amount)
        => amount >= 0 && FuelUsed <= Limits.MaxFuel - amount;

    public void ChargeAllocation(long bytes)
        => ChargeByteCounter(ref _allocatedBytes, bytes, Limits.MaxAllocatedBytes, "allocation budget exhausted");

    public void ChargeCollection(SandboxValue value) => ChargeCollection(value, CancellationToken.None);

    public void ChargeCollection(SandboxValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        ChargeMeasuredShape(SandboxValueShapeMeter.Measure(value, Limits, cancellationToken, this));
    }
    public void ChargeValue(SandboxValue value) => ChargeValue(value, CancellationToken.None);

    public void ChargeValue(SandboxValue value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is RecordValue && ValueShapeCache.TryGet(value, out var cachedRecordShape))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChargeMeasuredShape(cachedRecordShape);
            return;
        }

        if (ResourceFlatValueShapeMeter.TryMeasure(value, cancellationToken, out var flatShape))
        {
            ChargeMeasuredShape(flatShape);
            return;
        }

        ChargeMeasuredShape(ValueShapeCache.GetOrMeasure(value, cancellationToken, this));
    }

    internal void ChargeValueShape(ValueShape shape) => ChargeMeasuredShape(shape);

    public void ChargeString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var bytes = SandboxLiteralConstraints.StringByteCount(value.Length);
        ChargeStringShape(new ValueShape(0, 0, 0, 0, value.Length, bytes));
    }

    public void ChargeStringAllocation(int charLength)
    {
        var shape = ResourceMeterUsageCharges.GetStringAllocationShape(charLength);
        ChargeStringShape(shape);
    }

    internal bool CanChargeStringValues(string value, long count)
        => ResourceMeterUsageCharges.CanChargeStringValues(value, count, Limits, AllocatedBytes, StringBytes);

    internal void ChargeStringValues(string value, long count)
    {
        var usage = ResourceMeterUsageCharges.ChargeStringValues(value, count, Limits, AllocatedBytes, StringBytes);
        _allocatedBytes = usage.AllocatedBytes;
        StringBytes = usage.StringBytes;
    }

    public void ChargeHostCall(string bindingId, int? maxCallsPerRun = null)
    {
        HostCalls = ResourceHostCallTracker.AddHostCallCount(HostCalls, 1, bindingId);
        if (HostCalls > Limits.MaxHostCalls)
        {
            throw ResourceHostCallTracker.HostCallQuota(bindingId);
        }

        if (maxCallsPerRun is not null)
        {
            (_hostCallTracker ??= new ResourceHostCallTracker())
                .ChargeLimitedBindingCall(bindingId, maxCallsPerRun.Value);
        }
    }

    internal bool CanChargeHostCalls(long calls)
        => ResourceMeterUsageCharges.CanChargeHostCalls(HostCalls, Limits, calls);

    internal void ChargeHostCalls(string bindingId, long calls)
    {
        if (!CanChargeHostCalls(calls))
        {
            throw ResourceHostCallTracker.HostCallQuota(bindingId);
        }

        var count = checked((int)calls);
        HostCalls = ResourceHostCallTracker.AddHostCallCount(HostCalls, count, bindingId);
    }

    public void ChargeFileRead(long bytes)
        => ChargeByteCounter(ref _fileBytesRead, bytes, Limits.MaxFileBytesRead, "file read byte budget exhausted");

    public void ChargeFileWrite(long bytes)
        => ChargeByteCounter(ref _fileBytesWritten, bytes, Limits.MaxFileBytesWritten, "file write byte budget exhausted");

    public void ChargeNetworkRead(long bytes)
        => ChargeByteCounter(ref _networkBytesRead, bytes, Limits.MaxNetworkBytesRead, "network read byte budget exhausted");

    public void ChargeNetworkWrite(long bytes)
        => ChargeByteCounter(ref _networkBytesWritten, bytes, Limits.MaxNetworkBytesWritten, "network write byte budget exhausted");

    public void ChargeLogEvent(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogEvents = ResourceMeterUsageCharges.AddLogEvent(LogEvents, message, Limits);
        if (LogEvents > Limits.MaxLogEvents)
        {
            throw Quota("log event budget exhausted");
        }
    }

    public void CheckDeadline()
        => ResourceMeterDeadline.ThrowIfElapsed(_deadline);

    public TimeSpan RemainingWallTime()
        => ResourceMeterDeadline.RemainingWallTime(_deadline);

    internal long BeginWallTimeSuspension() => ResourceMeterDeadline.BeginSuspension(_deadline);

    internal void EndWallTimeSuspension(long startedAt)
        => _deadline = ResourceMeterDeadline.Extend(_deadline, startedAt);

    private void ChargeMeasuredShape(in ShapeInfo info)
    {
        var scanFuel = info.Nodes / 64;
        if (scanFuel > 0)
        {
            ChargeFuel(scanFuel);
            CheckDeadline();
        }

        ChargeMeasuredShape(info.Shape);
    }

    private void ChargeMeasuredShape(ValueShape shape)
    {
        ResourceMeterUsageCharges.ValidateCollectionShape(shape, Limits);
        CollectionElements = AddChecked(CollectionElements, shape.Elements, "collection element budget exhausted");
        if (CollectionElements > Limits.MaxTotalCollectionElements)
        {
            throw Quota("collection element budget exhausted");
        }

        ChargeStringShape(shape);
    }

    private void ChargeStringShape(ValueShape shape)
    {
        ResourceMeterUsageCharges.ValidateStringShape(shape, Limits);
        if (shape.StringBytes > 0)
        {
            ChargeAllocation(shape.StringBytes);
        }

        StringBytes = AddChecked(StringBytes, shape.StringBytes, "string byte budget exhausted");
        if (StringBytes > Limits.MaxTotalStringBytes)
        {
            throw Quota("string byte budget exhausted");
        }
    }

    private static void ChargeByteCounter(ref long current, long bytes, long max, string quotaMessage)
    {
        current = ResourceMeterUsageCharges.AddBytes(current, bytes, quotaMessage);
        if (current > max)
        {
            throw Quota(quotaMessage);
        }
    }
}
