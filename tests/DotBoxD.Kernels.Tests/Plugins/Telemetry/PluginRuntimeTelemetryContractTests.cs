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
                "Entrypoint",
                () => new PluginExecutionObservation(
                    "   ",
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
                "Entrypoint",
                () => ValidObservation() with { Entrypoint = "\t" }
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
            {
                "CacheStatus",
                () => new PluginExecutionObservation(
                    "Handle",
                    ExecutionMode.Interpreted,
                    ExecutionMode.Interpreted,
                    Succeeded: true,
                    ErrorCode: null,
                    FallbackReason: null,
                    CacheStatus: " ",
                    RuntimeForm: null,
                    CacheKey: null,
                    ArtifactHash: null,
                    MaterializationStatus: null)
            },
            {
                "CacheStatus",
                () => ValidObservation() with { CacheStatus = "\n" }
            },
        };

    public static TheoryData<Func<PluginExecutionObservation>> TerminalStateContradictions()
        => new()
        {
            () => new PluginExecutionObservation(
                "Handle",
                ExecutionMode.Interpreted,
                ExecutionMode.Interpreted,
                Succeeded: true,
                ErrorCode: SandboxErrorCode.PolicyDenied,
                FallbackReason: null,
                CacheStatus: "None",
                RuntimeForm: null,
                CacheKey: null,
                ArtifactHash: null,
                MaterializationStatus: null),
            () => ValidObservation() with { ErrorCode = SandboxErrorCode.PolicyDenied },
            () => new PluginExecutionObservation(
                "Handle",
                ExecutionMode.Interpreted,
                ExecutionMode.Interpreted,
                Succeeded: false,
                ErrorCode: null,
                FallbackReason: null,
                CacheStatus: "None",
                RuntimeForm: null,
                CacheKey: null,
                ArtifactHash: null,
                MaterializationStatus: null),
            () => ValidObservation() with { Succeeded = false },
        };

    public static TheoryData<string, Func<PluginExecutionObservation>> InterpretedSuccessCompiledTelemetryEnvelopes()
        => new()
        {
            {
                "CacheStatus",
                () => new PluginExecutionObservation(
                    "Handle",
                    ExecutionMode.Interpreted,
                    ExecutionMode.Interpreted,
                    Succeeded: true,
                    ErrorCode: null,
                    FallbackReason: null,
                    CacheStatus: "Hit",
                    RuntimeForm: "LoadedAssembly",
                    CacheKey: "cache-key",
                    ArtifactHash: "artifact-hash",
                    MaterializationStatus: "Miss")
            },
            { "CacheStatus", () => ValidObservation() with { CacheStatus = "Hit" } },
            { "RuntimeForm", () => ValidObservation() with { RuntimeForm = "LoadedAssembly" } },
            { "CacheKey", () => ValidObservation() with { CacheKey = "cache-key" } },
            { "ArtifactHash", () => ValidObservation() with { ArtifactHash = "artifact-hash" } },
            { "MaterializationStatus", () => ValidObservation() with { MaterializationStatus = "Miss" } },
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
    [MemberData(nameof(TerminalStateContradictions))]
    public void Plugin_execution_observation_rejects_terminal_state_contradictions(
        Func<PluginExecutionObservation> create)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _ = create());

        Assert.Contains(exception.ParamName, new[] { nameof(PluginExecutionObservation.ErrorCode), nameof(PluginExecutionObservation.Succeeded) });
    }

    [Theory]
    [MemberData(nameof(InterpretedSuccessCompiledTelemetryEnvelopes))]
    public void Plugin_execution_observation_rejects_interpreted_success_compiled_telemetry(
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

    [Fact]
    public void Result_hook_fault_has_no_zero_initialized_contract_instance()
    {
        ResultHookFault fault = default!;

        Assert.Null(fault);
    }

    [Fact]
    public void Subscription_delivery_fault_has_no_zero_initialized_contract_instance()
    {
        SubscriptionDeliveryFault fault = default!;

        Assert.Null(fault);
    }

    [Fact]
    public void Plugin_execution_observation_accepts_valid_required_text()
    {
        var observation = ValidObservation();

        Assert.Equal("Handle", observation.Entrypoint);
        Assert.Equal("None", observation.CacheStatus);
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
