using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarAssignmentScenarios
{
    private static readonly int[] AssignmentCounts = [0, 1, 4, 8];
    private static readonly ScalarAssignmentType[] ScalarTypes =
        [ScalarAssignmentType.I32, ScalarAssignmentType.I64, ScalarAssignmentType.F64];

    public static async Task<InterpreterScalarAssignmentLane[]> PrepareAsync(
        SandboxHost host,
        SandboxPolicy policy)
    {
        var lanes = new List<InterpreterScalarAssignmentLane>();
        foreach (var type in ScalarTypes)
        {
            lanes.Add(await PrepareRecurrenceLaneAsync(
                host, policy, type, ScalarAssignmentRhs.Literal));
            lanes.Add(await PrepareRecurrenceLaneAsync(
                host, policy, type, ScalarAssignmentRhs.RawVariable));
        }

        foreach (var type in ScalarTypes)
        {
            lanes.Add(await PrepareControlLaneAsync(
                host,
                policy,
                type,
                "boxed target",
                InterpreterScalarAssignmentModules.CreateBoxed,
                SandboxValue.FromList([], ScalarType(type)),
                static count => count == 0 ? 0 : 42,
                static count => Usage(7 + (4L * count), collectionElements: 0)));
            lanes.Add(await PrepareControlLaneAsync(
                host,
                policy,
                type,
                "evaluator miss",
                InterpreterScalarAssignmentModules.CreateEvaluatorMiss,
                Scalar(type, 1),
                static count => count + 1D,
                static count => Usage(3 + (8L * count), collectionElements: 0)));
        }

        return lanes.ToArray();
    }

    private static async Task<InterpreterScalarAssignmentLane> PrepareRecurrenceLaneAsync(
        SandboxHost host,
        SandboxPolicy policy,
        ScalarAssignmentType type,
        ScalarAssignmentRhs rhs)
    {
        var cases = await PrepareCasesAsync(
            host,
            policy,
            type,
            (scalarType, count) => InterpreterScalarAssignmentModules.Create(scalarType, rhs, count),
            static count => count + 1D,
            count => Usage(
                3 + (4L * count),
                rhs == ScalarAssignmentRhs.Literal ? 0 : 2));
        var input = rhs == ScalarAssignmentRhs.Literal
            ? Scalar(type, 1)
            : SandboxValue.FromList([Scalar(type, 1), Scalar(type, 1)], ScalarType(type));
        var rhsName = rhs == ScalarAssignmentRhs.Literal ? "literal" : "raw variable";
        return new InterpreterScalarAssignmentLane($"{type} {rhsName}", type, input, cases);
    }

    private static async Task<InterpreterScalarAssignmentLane> PrepareControlLaneAsync(
        SandboxHost host,
        SandboxPolicy policy,
        ScalarAssignmentType type,
        string name,
        Func<ScalarAssignmentType, int, string> createModule,
        SandboxValue input,
        Func<int, double> expectedValue,
        Func<int, SandboxResourceUsage> expectedUsage)
        => new(
            $"{type} {name}",
            type,
            input,
            await PrepareCasesAsync(
                host,
                policy,
                type,
                createModule,
                expectedValue,
                expectedUsage));

    private static async Task<InterpreterScalarAssignmentCase[]> PrepareCasesAsync(
        SandboxHost host,
        SandboxPolicy policy,
        ScalarAssignmentType type,
        Func<ScalarAssignmentType, int, string> createModule,
        Func<int, double> expectedValue,
        Func<int, SandboxResourceUsage> expectedUsage)
    {
        var cases = new InterpreterScalarAssignmentCase[AssignmentCounts.Length];
        for (var index = 0; index < AssignmentCounts.Length; index++)
        {
            var assignmentCount = AssignmentCounts[index];
            var module = await host.ImportJsonAsync(createModule(type, assignmentCount));
            cases[index] = new InterpreterScalarAssignmentCase(
                assignmentCount,
                await host.PrepareAsync(module, policy),
                expectedValue(assignmentCount),
                expectedUsage(assignmentCount));
        }

        return cases;
    }

    private static SandboxValue Scalar(ScalarAssignmentType type, double value)
        => type switch
        {
            ScalarAssignmentType.I32 => SandboxValue.FromInt32((int)value),
            ScalarAssignmentType.I64 => SandboxValue.FromInt64((long)value),
            ScalarAssignmentType.F64 => SandboxValue.FromDouble(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static SandboxType ScalarType(ScalarAssignmentType type)
        => type switch
        {
            ScalarAssignmentType.I32 => SandboxType.I32,
            ScalarAssignmentType.I64 => SandboxType.I64,
            ScalarAssignmentType.F64 => SandboxType.F64,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static SandboxResourceUsage Usage(long fuel, long collectionElements)
        => new(
            FuelUsed: fuel,
            MaxFuel: long.MaxValue,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: collectionElements,
            StringBytes: 0);
}

internal readonly record struct InterpreterScalarAssignmentCase(
    int AssignmentCount,
    ExecutionPlan Plan,
    double ExpectedValue,
    SandboxResourceUsage ExpectedUsage);

internal sealed record InterpreterScalarAssignmentLane(
    string Name,
    ScalarAssignmentType Type,
    SandboxValue Input,
    InterpreterScalarAssignmentCase[] Cases);
