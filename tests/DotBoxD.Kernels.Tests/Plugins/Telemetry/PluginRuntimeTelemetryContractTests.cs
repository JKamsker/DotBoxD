using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginRuntimeTelemetryContractTests
{
    public static TheoryData<string, Func<PluginExecutionObservation>> InvalidExecutionObservations()
        => new()
        {
            {
                "Entrypoint",
                () => new PluginExecutionObservation(
                    null!,
                    ExecutionMode.Interpreted,
                    ExecutionMode.Interpreted,
                    Succeeded: true,
                    ErrorCode: null,
                    FallbackReason: null,
                    CacheStatus: "None",
                    RuntimeForm: null,
                    CacheKey: null,
                    ArtifactHash: null,
                    MaterializationStatus: null)
            },
            {
                "RequestedMode",
                () => ValidObservation() with { RequestedMode = (ExecutionMode)999 }
            },
            {
                "ActualMode",
                () => ValidObservation() with { ActualMode = (ExecutionMode)998 }
            },
            {
                "ErrorCode",
                () => ValidObservation() with { ErrorCode = (SandboxErrorCode)999 }
            },
            {
                "FallbackReason",
                () => ValidObservation() with { FallbackReason = (SandboxErrorCode)998 }
            },
            {
                "CacheStatus",
                () => new PluginExecutionObservation(
                    "Handle",
                    ExecutionMode.Interpreted,
                    ExecutionMode.Interpreted,
                    Succeeded: true,
                    ErrorCode: null,
                    FallbackReason: null,
                    CacheStatus: null!,
                    RuntimeForm: null,
                    CacheKey: null,
                    ArtifactHash: null,
                    MaterializationStatus: null)
            },
        };

    public static TheoryData<string, Func<ResultHookFault>> InvalidResultHookFaults()
        => new()
        {
            { "EventType", () => new ResultHookFault(null!, new InvalidOperationException("boom")) },
            { "Exception", () => new ResultHookFault(typeof(PluginRuntimeTelemetryContractTests), null!) },
        };

    public static TheoryData<string, Func<SubscriptionDeliveryFault>> InvalidSubscriptionDeliveryFaults()
        => new()
        {
            {
                "EventType",
                () => new SubscriptionDeliveryFault(
                    null!,
                    SubscriptionDeliveryStage.Handler,
                    new InvalidOperationException("boom"))
            },
            {
                "Stage",
                () => new SubscriptionDeliveryFault(
                    typeof(PluginRuntimeTelemetryContractTests),
                    (SubscriptionDeliveryStage)999,
                    new InvalidOperationException("boom"))
            },
            {
                "Exception",
                () => new SubscriptionDeliveryFault(
                    typeof(PluginRuntimeTelemetryContractTests),
                    SubscriptionDeliveryStage.Handler,
                    null!)
            },
        };

    [Theory]
    [MemberData(nameof(InvalidExecutionObservations))]
    public void Plugin_execution_observation_rejects_malformed_contract_values(
        string memberName,
        Func<PluginExecutionObservation> create)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _ = create());

        Assert.Equal(memberName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidResultHookFaults))]
    public void Result_hook_fault_rejects_malformed_contract_values(
        string memberName,
        Func<ResultHookFault> create)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _ = create());

        Assert.Equal(memberName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidSubscriptionDeliveryFaults))]
    public void Subscription_delivery_fault_rejects_malformed_contract_values(
        string memberName,
        Func<SubscriptionDeliveryFault> create)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _ = create());

        Assert.Equal(memberName, exception.ParamName);
    }

    private static PluginExecutionObservation ValidObservation()
        => new(
            "Handle",
            ExecutionMode.Interpreted,
            ExecutionMode.Interpreted,
            Succeeded: true,
            ErrorCode: null,
            FallbackReason: null,
            CacheStatus: "None",
            RuntimeForm: null,
            CacheKey: null,
            ArtifactHash: null,
            MaterializationStatus: null);
}
