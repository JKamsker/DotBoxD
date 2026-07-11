using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class ExecutionAuditModelRangeContractTests
{
    [Theory]
    [InlineData("FuelUsed")]
    [InlineData("MaxFuel")]
    [InlineData("LoopIterations")]
    [InlineData("AllocatedBytes")]
    [InlineData("HostCalls")]
    [InlineData("FileBytesRead")]
    [InlineData("FileBytesWritten")]
    [InlineData("NetworkBytesRead")]
    [InlineData("NetworkBytesWritten")]
    [InlineData("LogEvents")]
    [InlineData("CollectionElements")]
    [InlineData("StringBytes")]
    public void Resource_usage_rejects_negative_counters(string memberName)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CreateUsageWithNegative(memberName));

        Assert.Equal(memberName, ex.ParamName);
    }

    [Fact]
    public void Audit_event_rejects_negative_bytes()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SandboxAuditEvent(
            SandboxRunId.New(),
            BindingAuditKinds.BindingCall,
            DateTimeOffset.UtcNow,
            true,
            Bytes: -1));

        Assert.Equal("Bytes", ex.ParamName);
    }

    [Fact]
    public void Execution_result_rejects_undefined_actual_mode()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new SandboxExecutionResult
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = ValidUsage(),
            AuditEvents = [],
            ActualMode = (ExecutionMode)999,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy"
        });

        Assert.Equal("ActualMode", ex.ParamName);
    }

    [Fact]
    public void Audit_event_rejects_undefined_effect_bits()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new SandboxAuditEvent(
            SandboxRunId.New(),
            BindingAuditKinds.BindingCall,
            DateTimeOffset.UtcNow,
            true,
            Effect: (SandboxEffect)(1 << 30)));

        Assert.Equal("Effect", ex.ParamName);
    }

    [Fact]
    public void Audit_event_rejects_undefined_error_code()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new SandboxAuditEvent(
            SandboxRunId.New(),
            BindingAuditKinds.BindingCall,
            DateTimeOffset.UtcNow,
            false,
            ErrorCode: (SandboxErrorCode)999));

        Assert.Equal("ErrorCode", ex.ParamName);
    }

    private static SandboxResourceUsage CreateUsageWithNegative(string memberName)
        => memberName switch
        {
            "FuelUsed" => Usage(fuelUsed: -1),
            "MaxFuel" => Usage(maxFuel: -1),
            "LoopIterations" => Usage(loopIterations: -1),
            "AllocatedBytes" => Usage(allocatedBytes: -1),
            "HostCalls" => Usage(hostCalls: -1),
            "FileBytesRead" => Usage(fileBytesRead: -1),
            "FileBytesWritten" => Usage(fileBytesWritten: -1),
            "NetworkBytesRead" => Usage(networkBytesRead: -1),
            "NetworkBytesWritten" => Usage(networkBytesWritten: -1),
            "LogEvents" => Usage(logEvents: -1),
            "CollectionElements" => Usage(collectionElements: -1),
            "StringBytes" => Usage(stringBytes: -1),
            _ => throw new ArgumentOutOfRangeException(nameof(memberName), memberName, null)
        };

    private static SandboxResourceUsage ValidUsage()
        => Usage();

    private static SandboxResourceUsage Usage(
        long fuelUsed = 0,
        long maxFuel = 100,
        long loopIterations = 0,
        long allocatedBytes = 0,
        int hostCalls = 0,
        long fileBytesRead = 0,
        long fileBytesWritten = 0,
        long networkBytesRead = 0,
        long networkBytesWritten = 0,
        int logEvents = 0,
        long collectionElements = 0,
        long stringBytes = 0)
        => new(
            fuelUsed,
            maxFuel,
            loopIterations,
            allocatedBytes,
            hostCalls,
            fileBytesRead,
            fileBytesWritten,
            networkBytesRead,
            networkBytesWritten,
            logEvents,
            collectionElements,
            stringBytes);
}
