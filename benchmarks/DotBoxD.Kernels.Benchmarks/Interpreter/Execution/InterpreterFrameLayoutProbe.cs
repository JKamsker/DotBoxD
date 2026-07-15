using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterFrameLayoutProbe
{
    private const int WarmupIterations = 1_000;
    private const int Iterations = 50_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter frame-layout executions = {Iterations:N0}");
        Console.WriteLine("case                     total ms      allocated B       B/op     F/L/A/H   checksum");
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "zero parameter control",
            InterpreterFrameLayoutModules.ZeroParameters,
            SandboxValue.Unit,
            expectedValue: 1,
            ExpectedUsage(fuelUsed: 3));
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "one raw parameter only",
            InterpreterFrameLayoutModules.OneRawParameter,
            SandboxValue.FromInt32(1),
            expectedValue: 1,
            ExpectedUsage(fuelUsed: 3));
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "eight local chain",
            InterpreterFrameLayoutModules.EightLocalChain,
            SandboxValue.FromInt32(1),
            expectedValue: 37,
            ExpectedUsage(fuelUsed: 35));
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "raw parameter + boxed local",
            InterpreterFrameLayoutModules.RawParameterAndBoxedLocal,
            SandboxValue.FromInt32(1),
            expectedValue: 1,
            ExpectedUsage(fuelUsed: 5, allocatedBytes: 20, stringBytes: 20));
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "raw local state control",
            InterpreterFrameLayoutModules.GenuineRawLocal,
            SandboxValue.Unit,
            expectedValue: 7,
            ExpectedUsage(fuelUsed: 5),
            verifyRawLocalState: true);
        await RunCaseAsync(
            host,
            interpreter,
            options,
            policy,
            "two raw parameters only",
            InterpreterFrameLayoutModules.TwoRawParameters,
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)], SandboxType.I32),
            expectedValue: 3,
            ExpectedUsage(fuelUsed: 5, collectionElements: 2));
    }

    private static async Task RunCaseAsync(
        SandboxHost host,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        SandboxPolicy policy,
        string name,
        string moduleJson,
        SandboxValue input,
        int expectedValue,
        SandboxResourceUsage expectedUsage,
        bool verifyRawLocalState = false)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);

        _ = Measure(interpreter, plan, input, options, expectedValue, expectedUsage, WarmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, input, options, expectedValue, expectedUsage, Iterations);
        Console.WriteLine(
            $"{name,-24} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,16:N0} "
            + $"{measurement.Bytes / (double)Iterations,10:N1} "
            + $"{measurement.FuelUsed:N0}/{measurement.LoopIterations:N0}/"
            + $"{measurement.SandboxAllocatedBytes:N0}/{measurement.HostCalls:N0} "
            + $"{measurement.Checksum,10:N0}");

        if (verifyRawLocalState)
        {
            await VerifyUnassignedRawLocalAsync(host, interpreter, plan, options);
        }
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int expectedValue,
        SandboxResourceUsage expectedUsage,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "frame-layout probe unexpectedly left the synchronous interpreter path");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            var value = ((I32Value)result.Value!).Value;
            if (value != expectedValue)
            {
                throw new InvalidOperationException($"expected result {expectedValue}, received {value}");
            }

            checksum += value;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException(
                    $"resource usage changed: expected {expectedUsage}, received {result.ResourceUsage}");
            }
        }

        watch.Stop();
        var expectedChecksum = (long)expectedValue * iterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, received {checksum}");
        }

        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage.FuelUsed,
            expectedUsage.LoopIterations,
            expectedUsage.AllocatedBytes,
            expectedUsage.HostCalls);
    }

    private static async Task VerifyUnassignedRawLocalAsync(
        SandboxHost host,
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var unassignedModule = await host.ImportJsonAsync(InterpreterFrameLayoutModules.UnassignedRawLocal);
        var tampered = new ExecutionPlan(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            unassignedModule,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);
        var pending = interpreter.ExecuteAsync(tampered, "main", SandboxValue.Unit, options, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("raw-local control unexpectedly left the synchronous interpreter path");
        }

        var result = pending.Result;
        if (result.Succeeded ||
            result.Error is not { Code: SandboxErrorCode.ValidationError, SafeMessage: "local 'value' read before assignment" })
        {
            throw new InvalidOperationException("raw-local control did not preserve read-before-assignment validation");
        }
    }

    private static SandboxResourceUsage ExpectedUsage(
        long fuelUsed,
        long allocatedBytes = 0,
        long collectionElements = 0,
        long stringBytes = 0)
        => new(
            fuelUsed,
            long.MaxValue,
            LoopIterations: 0,
            allocatedBytes,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            collectionElements,
            stringBytes);

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long Bytes,
        long Checksum,
        long FuelUsed,
        long LoopIterations,
        long SandboxAllocatedBytes,
        int HostCalls);
}
