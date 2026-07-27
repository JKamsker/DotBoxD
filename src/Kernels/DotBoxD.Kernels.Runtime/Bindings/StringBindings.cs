using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class StringBindings
{
    public static IReadOnlyList<BindingDescriptor> All { get; } = [
        Pure(
            "int32.toStringInvariant",
            [SandboxType.I32],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            BindingCostModel.Fixed(2),
            (ctx, args, _) => ValueTask.FromResult(CompiledRuntime.Int32ToStringInvariant(ctx, args[0])),
            nameof(CompiledRuntime.Int32ToStringInvariant)),
        Pure(
            "string.length",
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(1),
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromInt32(String(args[0]).Length)),
            nameof(CompiledRuntime.StringLength)),
        Pure(
            "string.isEmpty",
            [SandboxType.String],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(1),
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromBool(String(args[0]).Length == 0))),
        Pure(
            "string.substringBudgeted",
            [SandboxType.String, SandboxType.I32, SandboxType.I32],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            BindingCostModel.PerByte(2, 1),
            SubstringBudgetedInvoker.Instance.Invoke),
        Pure(
            "string.concatBudgeted",
            [SandboxType.String, SandboxType.String],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            BindingCostModel.PerByte(2, 1),
            (ctx, args, _) => {
                var text = ctx.CreateChargedStringConcat(
                    String(args[0]),
                    String(args[1]));
                return ValueTask.FromResult(SandboxValue.FromString(text));
            }),
        Pure(
            "string.equals",
            [SandboxType.String, SandboxType.String],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(2),
            (ctx, args, _) => {
                var left = String(args[0]);
                var right = String(args[1]);
                ctx.ChargeFuel(CheckedCharCount(left, right));
                return ValueTask.FromResult(SandboxValue.FromBool(string.Equals(left, right, StringComparison.Ordinal)));
            }),
        Pure(
            "string.compareOrdinal",
            [SandboxType.String, SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(2),
            (ctx, args, _) => {
                var left = String(args[0]);
                var right = String(args[1]);
                ctx.ChargeFuel(CheckedCharCount(left, right));
                return ValueTask.FromResult(SandboxValue.FromInt32(Math.Sign(string.CompareOrdinal(left, right))));
            })
    ];

    private static BindingDescriptor Pure(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        SandboxEffect effects,
        BindingCostModel cost,
        BindingInvoker invoke,
        string compiledMethod = nameof(CompiledRuntime.CallBinding))
        => new(
            id,
            SemVersion.One,
            parameters,
            returnType,
            effects,
            null,
            cost,
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, compiledMethod));

    private static string String(SandboxValue value) => ((StringValue)value).Value;

    private static int I32(SandboxValue value) => ((I32Value)value).Value;

    private static long CheckedCharCount(string left, string right)
    {
        try
        {
            return checked((long)left.Length + right.Length);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, "string CPU budget exhausted"));
        }
    }

    private sealed class SubstringBudgetedInvoker : IThreeArgumentBindingInvoker
    {
        public static SubstringBudgetedInvoker Instance { get; } = new();

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
            => Invoke(context, args[0], args[1], args[2], cancellationToken);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            SandboxValue arg2,
            CancellationToken cancellationToken)
        {
            var text = context.CreateChargedSubstring(String(arg0), I32(arg1), I32(arg2));
            return ValueTask.FromResult(SandboxValue.FromString(text));
        }
    }
}
