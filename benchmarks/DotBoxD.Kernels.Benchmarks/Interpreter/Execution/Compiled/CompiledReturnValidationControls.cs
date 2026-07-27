using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledReturnValidationControls
{
    private const string DirectMessage = "probe malformed direct value";
    private const string CompiledMessage = "function return type mismatch";

    public static MalformedValidationControlResult Run()
    {
        var cases = CreateCases();
        using var context = CreateContext();
        long checksum = 0;
        foreach (var control in cases)
        {
            ExpectRejected(
                () => SandboxValueValidator.RequireType(
                    control.Value,
                    control.ExpectedType,
                    DirectMessage),
                DirectMessage,
                control.Name);
            checksum += control.ChecksumContribution;

            ExpectRejected(
                () => CompiledRuntime.RequireValueTypeAndRecordValidation(
                    context,
                    control.Value,
                    control.ExpectedType),
                CompiledMessage,
                control.Name);
            if (context.TryConsumeCompiledReturnValidation(control.Value, control.ExpectedType))
            {
                throw new InvalidOperationException(
                    $"malformed compiled return published a validation proof for '{control.Name}'");
            }

            checksum += control.ChecksumContribution;
        }

        return new MalformedValidationControlResult(cases.Count, cases.Count, checksum);
    }

    private static IReadOnlyList<MalformedValidationControl> CreateCases()
        => [
            WrongI32Item("wrong first item", invalidIndex: 0, checksumContribution: 1),
            WrongI32Item("wrong middle item", invalidIndex: 4, checksumContribution: 2),
            WrongI32Item("wrong last item", invalidIndex: 8, checksumContribution: 3),
            new(
                "non-finite F64 item",
                new ListValue([new F64Value(double.NaN)], SandboxType.F64),
                SandboxType.List(SandboxType.F64),
                4),
            new(
                "null String item",
                new ListValue([CreateMalformedString()], SandboxType.String),
                SandboxType.List(SandboxType.String),
                5)
        ];

    private static MalformedValidationControl WrongI32Item(
        string name,
        int invalidIndex,
        long checksumContribution)
    {
        var values = new SandboxValue[9];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = SandboxValue.FromInt32(i);
        }

        values[invalidIndex] = SandboxValue.FromString("wrong");
        return new MalformedValidationControl(
            name,
            new ListValue(values, SandboxType.I32),
            SandboxType.List(SandboxType.I32),
            checksumContribution);
    }

    private static StringValue CreateMalformedString()
        => (StringValue)RuntimeHelpers.GetUninitializedObject(typeof(StringValue));

    private static void ExpectRejected(Action action, string expectedMessage, string name)
    {
        try
        {
            action();
        }
        catch (SandboxRuntimeException exception) when (
            exception.Error.Code == SandboxErrorCode.InvalidInput &&
            StringComparer.Ordinal.Equals(exception.Error.SafeMessage, expectedMessage))
        {
            return;
        }

        throw new InvalidOperationException($"malformed validation control '{name}' was not rejected safely");
    }

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create().Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}

internal sealed record MalformedValidationControl(
    string Name,
    SandboxValue Value,
    SandboxType ExpectedType,
    long ChecksumContribution);

internal readonly record struct MalformedValidationControlResult(
    int DirectRejections,
    int CompiledRejections,
    long Checksum);
