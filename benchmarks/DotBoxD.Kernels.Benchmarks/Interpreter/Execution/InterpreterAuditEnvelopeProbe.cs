using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterAuditEnvelopeProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;
    private const int EnvelopeIterations = 1_000_000;
    private static readonly SandboxRunId ExplicitRunId = new(new Guid("83d21c63-70de-40bf-bf4c-e62f938df86d"));

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddLogBindings();
            builder.UseInterpreter();
        });
        var purePolicy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var logPolicy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxLogEvents(int.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var successPlan = await PrepareAsync(host, InterpreterAuditEnvelopeModules.PureSuccess, purePolicy);
        var failurePlan = await PrepareAsync(host, InterpreterAuditEnvelopeModules.PureFailure, purePolicy);
        var bindingPlan = await PrepareAsync(host, InterpreterAuditEnvelopeModules.AuditedBinding, logPolicy);
        var interpreter = new SandboxInterpreter();

        RunEnvelopeConstruction();
        Console.WriteLine();
        Console.WriteLine($"interpreter audit-envelope executions = {Iterations:N0}");
        Console.WriteLine(
            "case                              total ms    allocated B       B/op   checksum  F/L/A/H/Log/Elem/String audit");
        RunCase(interpreter, successPlan, "suppressed pure, default run", Options(suppress: true),
            ExpectedOutcome.I32(7), PureUsage(), AuditShape.Empty);
        RunCase(interpreter, successPlan, "suppressed pure, explicit run", Options(suppress: true, ExplicitRunId),
            ExpectedOutcome.I32(7), PureUsage(), AuditShape.Empty);
        RunCase(interpreter, successPlan, "audited pure, default run", Options(suppress: false),
            ExpectedOutcome.I32(7), PureUsage(), AuditShape.SuccessSummary);
        RunCase(interpreter, successPlan, "audited pure, explicit run", Options(suppress: false, ExplicitRunId),
            ExpectedOutcome.I32(7), PureUsage(), AuditShape.SuccessSummaryWithExplicitRunId);
        RunCase(interpreter, failurePlan, "suppressed failure control", Options(suppress: true),
            ExpectedOutcome.Failure(SandboxErrorCode.InvalidInput), PureUsage(fuel: 5), AuditShape.FailureSummary);
        RunCase(interpreter, successPlan, "suppressed debug control", Options(suppress: true, debug: true),
            ExpectedOutcome.I32(7), PureUsage(), AuditShape.DebugTrace);
        RunCase(interpreter, bindingPlan, "suppressed binding control", Options(suppress: true),
            ExpectedOutcome.Unit(), BindingUsage(), AuditShape.SandboxLog);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxExecutionOptions Options(
        bool suppress,
        SandboxRunId? runId = null,
        bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = suppress,
            RunId = runId,
            EnableDebugTrace = debug
        };

    private static ResourceUsageInvariant PureUsage(long fuel = 3)
        => new(fuel, long.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static ResourceUsageInvariant BindingUsage()
        => new(6, long.MaxValue, 0, 8, 1, 0, 0, 0, 0, 1, 0, 8);

    private static void RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        string name,
        SandboxExecutionOptions options,
        ExpectedOutcome outcome,
        ResourceUsageInvariant expectedUsage,
        AuditShape auditShape)
    {
        _ = Measure(interpreter, plan, options, outcome, expectedUsage, auditShape, WarmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, options, outcome, expectedUsage, auditShape, Iterations);
        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-33} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)Iterations,10:N1} {measurement.Checksum,10:N0} " +
            $"{usage.FuelUsed}/{usage.LoopIterations}/{usage.AllocatedBytes}/{usage.HostCalls}/" +
            $"{usage.LogEvents}/{usage.CollectionElements}/{usage.StringBytes} {auditShape}");
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ExpectedOutcome outcome,
        ResourceUsageInvariant pinnedUsage,
        AuditShape auditShape,
        int iterations)
    {
        long checksum = 0;
        ResourceUsageInvariant? observedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", SandboxValue.Unit, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("audit-envelope probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            checksum += outcome.Validate(result);
            ValidateAudit(result.AuditEvents, auditShape, plan);
            var usage = ResourceUsageInvariant.From(result.ResourceUsage);
            observedUsage ??= usage;
            if (usage != observedUsage.Value || usage != pinnedUsage)
            {
                throw new InvalidOperationException($"expected resource usage {pinnedUsage}, got {usage}");
            }
        }

        watch.Stop();
        var expectedChecksum = checked(outcome.Checksum * iterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, got {checksum}");
        }

        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            observedUsage ?? default);
    }

    private static void ValidateAudit(
        IReadOnlyList<SandboxAuditEvent> events,
        AuditShape shape,
        ExecutionPlan plan)
    {
        var expectedCount = shape == AuditShape.Empty ? 0 : shape == AuditShape.DebugTrace ? 2 : 1;
        if (events.Count != expectedCount)
        {
            throw new InvalidOperationException($"expected {expectedCount} audit events for {shape}, got {events.Count}");
        }

        SandboxRunId? runId = null;
        for (var i = 0; i < events.Count; i++)
        {
            var audit = events[i];
            runId ??= audit.RunId;
            if (audit.SequenceNumber != i + 1 || audit.RunId != runId)
            {
                throw new InvalidOperationException("audit sequence or run identity changed");
            }

            if (shape == AuditShape.SuccessSummaryWithExplicitRunId && audit.RunId != ExplicitRunId ||
                shape != AuditShape.SuccessSummaryWithExplicitRunId && audit.RunId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("audit run identity did not match the expected envelope");
            }
        }

        var matches = shape switch
        {
            AuditShape.Empty => true,
            AuditShape.SuccessSummary or AuditShape.SuccessSummaryWithExplicitRunId =>
                events[0] is { Kind: "RunSummary", Success: true, ErrorCode: null } summary &&
                summary.ResourceId == $"module:{plan.ModuleHash}",
            AuditShape.FailureSummary =>
                events[0] is
                {
                    Kind: "RunSummary",
                    Success: false,
                    ErrorCode: SandboxErrorCode.InvalidInput
                } failure && failure.ResourceId == $"module:{plan.ModuleHash}",
            AuditShape.DebugTrace => IsDebugTrace(events),
            AuditShape.SandboxLog =>
                events[0] is
                {
                    Kind: "SandboxLog",
                    Success: true,
                    BindingId: "log.info",
                    CapabilityId: "log.write",
                    ResourceId: "log:info",
                    Message: "ok"
                },
            _ => false
        };
        if (!matches)
        {
            throw new InvalidOperationException($"audit events did not match {shape}");
        }
    }

    private static bool IsDebugTrace(IReadOnlyList<SandboxAuditEvent> events)
        => events[0] is { Kind: "DebugTrace", Success: true, Message: var statement } &&
           statement!.Contains("node=statement:ReturnStatement", StringComparison.Ordinal) &&
           events[1] is { Kind: "DebugTrace", Success: true, Message: var expression } &&
           expression!.Contains("node=expression:LiteralExpression", StringComparison.Ordinal);

    private static void RunEnvelopeConstruction()
    {
        var real = MeasureEnvelope(lazy: false);
        var lazy = MeasureEnvelope(lazy: true);
        Console.WriteLine($"audit-envelope constructions = {EnvelopeIterations:N0}");
        Console.WriteLine("case                         total ms    allocated B       B/op   checksum");
        WriteEnvelope("real RunId + memory sink", real);
        WriteEnvelope("lazy uninitialized envelope", lazy);
    }

    private static EnvelopeMeasurement MeasureEnvelope(bool lazy)
    {
        _ = MeasureEnvelopeCore(lazy, WarmupIterations);
        ForceGc();
        return MeasureEnvelopeCore(lazy, EnvelopeIterations);
    }

    private static EnvelopeMeasurement MeasureEnvelopeCore(bool lazy, int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var runId = lazy ? null : SandboxRunId.New();
            IAuditSink? audit = lazy ? null : new InMemoryAuditSink();
            checksum += runId is null || runId.Value == Guid.Empty ? 0 : 1;
            checksum += audit?.EventsWritten ?? 0;
            GC.KeepAlive(runId);
            GC.KeepAlive(audit);
        }

        watch.Stop();
        var expectedChecksum = lazy ? 0 : iterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected envelope checksum {expectedChecksum}, got {checksum}");
        }

        return new EnvelopeMeasurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void WriteEnvelope(string name, EnvelopeMeasurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)EnvelopeIterations,10:N1} {measurement.Checksum,10:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

}
