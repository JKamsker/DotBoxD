using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class ExecutionAuditModelContractTests
{
    [Theory]
    [InlineData("ResourceUsage")]
    [InlineData("ModuleHash")]
    [InlineData("PlanHash")]
    [InlineData("PolicyHash")]
    public void Execution_result_rejects_null_required_contract_members(string memberName)
    {
        AssertPublicArgumentException(memberName, () => _ = memberName switch
        {
            "ResourceUsage" => ValidExecutionResult() with { ResourceUsage = null! },
            "ModuleHash" => ValidExecutionResult() with { ModuleHash = null! },
            "PlanHash" => ValidExecutionResult() with { PlanHash = null! },
            "PolicyHash" => ValidExecutionResult() with { PolicyHash = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(memberName))
        });
    }

    [Fact]
    public void Execution_result_rejects_null_audit_events_collection()
    {
        AssertPublicArgumentException("AuditEvents", () => _ = ValidExecutionResult() with { AuditEvents = null! });
    }

    [Fact]
    public void Execution_result_rejects_null_audit_event_entries()
    {
        AssertPublicArgumentException("AuditEvents", () => _ = ValidExecutionResult() with
        {
            AuditEvents = [null!]
        });
    }

    [Theory]
    [InlineData("SuccessValueBeforeSucceeded", true)]
    [InlineData("FailureErrorBeforeSucceeded", false)]
    [InlineData("FailureSucceededBeforeError", false)]
    public void Execution_result_accepts_valid_terminal_state_initialization_orders(
        string terminalOrder,
        bool expectedSucceeded)
    {
        var result = terminalOrder switch
        {
            "SuccessValueBeforeSucceeded" => new SandboxExecutionResult
            {
                Value = SandboxValue.Unit,
                Succeeded = true,
                ResourceUsage = ValidUsage(),
                AuditEvents = [ValidAuditEvent()],
                ActualMode = ExecutionMode.Interpreted,
                ExecutionDispatched = true,
                ModuleHash = "module",
                PlanHash = "plan",
                PolicyHash = "policy"
            },
            "FailureErrorBeforeSucceeded" => new SandboxExecutionResult
            {
                Error = ValidError(),
                Succeeded = false,
                ResourceUsage = ValidUsage(),
                AuditEvents = [ValidAuditEvent()],
                ActualMode = ExecutionMode.Interpreted,
                ExecutionDispatched = true,
                ModuleHash = "module",
                PlanHash = "plan",
                PolicyHash = "policy"
            },
            "FailureSucceededBeforeError" => new SandboxExecutionResult
            {
                Succeeded = false,
                Error = ValidError(),
                ResourceUsage = ValidUsage(),
                AuditEvents = [ValidAuditEvent()],
                ActualMode = ExecutionMode.Interpreted,
                ExecutionDispatched = true,
                ModuleHash = "module",
                PlanHash = "plan",
                PolicyHash = "policy"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(terminalOrder))
        };

        Assert.Equal(expectedSucceeded, result.Succeeded);
    }

    [Theory]
    [InlineData("SuccessWithError", "Error")]
    [InlineData("FailureWithValue", "Value")]
    [InlineData("FailureWithoutError", "Error")]
    public void Execution_result_rejects_contradictory_terminal_state(string terminalState, string memberName)
    {
        AssertPublicArgumentException(memberName, () => _ = terminalState switch
        {
            "SuccessWithError" => ValidExecutionResult() with { Error = ValidError() },
            "FailureWithValue" => ValidExecutionResult() with
            {
                Succeeded = false,
                Error = ValidError()
            },
            "FailureWithoutError" => ValidFailedExecutionResult() with { Error = null },
            _ => throw new ArgumentOutOfRangeException(nameof(terminalState))
        });
    }

    [Theory]
    [InlineData("RunId")]
    [InlineData("Kind")]
    public void Audit_event_rejects_null_required_contract_members(string memberName)
    {
        AssertPublicArgumentException(memberName, () => _ = memberName switch
        {
            "RunId" => ValidAuditEvent() with { RunId = null! },
            "Kind" => ValidAuditEvent() with { Kind = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(memberName))
        });
    }

    [Fact]
    public void Audit_event_rejects_null_field_values()
    {
        AssertPublicArgumentException("Fields", () => _ = ValidAuditEvent() with
        {
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceKind"] = null!
            }
        });
    }

    [Fact]
    public void In_memory_audit_sink_rejects_null_events()
    {
        var sink = new InMemoryAuditSink();

        AssertPublicArgumentException("auditEvent", () => sink.Write(null!));
    }

    private static SandboxExecutionResult ValidExecutionResult()
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)).Snapshot(),
            AuditEvents = [ValidAuditEvent()],
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy"
        };

    private static SandboxResourceUsage ValidUsage()
        => new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)).Snapshot();

    private static SandboxExecutionResult ValidFailedExecutionResult()
        => ValidExecutionResult() with
        {
            Value = null,
            Succeeded = false,
            Error = ValidError()
        };

    private static SandboxError ValidError()
        => new(SandboxErrorCode.InvalidInput, "invalid input");

    private static SandboxAuditEvent ValidAuditEvent()
        => new(
            SandboxRunId.New(),
            BindingAuditKinds.BindingCall,
            DateTimeOffset.UtcNow,
            true,
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceKind"] = "test",
                ["durationMs"] = "0",
                ["moduleHash"] = "module",
                ["policyHash"] = "policy"
            });

    private static void AssertPublicArgumentException(string expectedParamName, Action action)
    {
        var exception = Record.Exception(action);
        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);

        Assert.Equal(expectedParamName, argumentException.ParamName);
    }
}
