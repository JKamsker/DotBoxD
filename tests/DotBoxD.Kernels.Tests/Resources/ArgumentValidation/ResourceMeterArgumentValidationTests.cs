using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Resources;

public sealed class ResourceMeterArgumentValidationTests
{
    [Theory]
    [MemberData(nameof(NullChargeArguments))]
    public void Resource_meter_charge_apis_reject_direct_null_arguments(
        Action<ResourceMeter> charge,
        string paramName)
    {
        var meter = new ResourceMeter(new ResourceLimits());

        var ex = Assert.Throws<ArgumentNullException>(() => charge(meter));

        Assert.Equal(paramName, ex.ParamName);
    }

    public static TheoryData<Action<ResourceMeter>, string> NullChargeArguments()
        => new()
        {
            { meter => meter.ChargeValue(null!), "value" },
            { meter => meter.ChargeCollection(null!), "value" },
            { meter => meter.ChargeString(null!), "value" },
            { meter => meter.ChargeLogEvent(null!), "message" }
        };
}
