using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class ConstrainedScalarWrapperNullTests
{
    [Theory]
    [MemberData(nameof(NullConstructorInputs))]
    public void Constrained_scalar_wrappers_reject_null_constructor_inputs(Action construct, string paramName)
    {
        var ex = Assert.Throws<ArgumentNullException>(construct);

        Assert.Equal(paramName, ex.ParamName);
    }

    public static TheoryData<Action, string> NullConstructorInputs()
        => new()
        {
            { () => _ = new OpaqueIdValue(null!, "id"), "TypeName" },
            { () => _ = new OpaqueIdValue("PlayerId", null!), "Value" },
            { () => _ = new SandboxPath(null!), "RelativePath" },
            { () => _ = new SandboxPathValue(null!), "Value" },
            { () => _ = new SandboxUri(null!), "Value" },
            { () => _ = new SandboxUriValue(null!), "Value" }
        };

    [Theory]
    [MemberData(nameof(NullInitializerInputs))]
    public void Constrained_scalar_wrappers_reject_null_initializer_inputs(Action initialize)
    {
        var ex = Assert.Throws<ArgumentNullException>(initialize);

        Assert.Equal("value", ex.ParamName);
    }

    public static TheoryData<Action> NullInitializerInputs()
        => new()
        {
            () => _ = new OpaqueIdValue("PlayerId", "id") { TypeName = null! },
            () => _ = new OpaqueIdValue("PlayerId", "id") { Value = null! },
            () => _ = new SandboxPath("config/settings.json") { RelativePath = null! },
            () => _ = new SandboxPathValue(new SandboxPath("config/settings.json")) { Value = null! },
            () => _ = new SandboxUri("https://example.test/config") { Value = null! },
            () => _ = new SandboxUriValue(new SandboxUri("https://example.test/config")) { Value = null! }
        };

    [Fact]
    public void Bypassed_null_opaque_id_type_name_does_not_leak_null_reference_from_type_inspection()
    {
        var value = Bypassed<OpaqueIdValue>();

        bool isKnown = default;
        var ex = Record.Exception(() => isKnown = value.Type.IsKnown());

        Assert.Null(ex);
        Assert.False(isKnown);
    }

    [Theory]
    [MemberData(nameof(BypassedNullWrapperValues))]
    public void Resource_meter_rejects_bypassed_null_wrapper_values_without_null_reference(SandboxValue value)
    {
        var meter = new ResourceMeter(new ResourceLimits());

        var ex = Assert.Throws<SandboxRuntimeException>(() => meter.ChargeValue(value));

        Assert.Equal(SandboxErrorCode.InvalidInput, ex.Error.Code);
    }

    public static TheoryData<SandboxValue> BypassedNullWrapperValues()
        => new()
        {
            Bypassed<OpaqueIdValue>(),
            Bypassed<SandboxPathValue>(),
            Bypassed<SandboxUriValue>()
        };

    private static T Bypassed<T>()
        where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
