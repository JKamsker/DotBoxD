namespace SafeIR.Runtime;

using SafeIR;

public static class MathBindings
{
    public static IReadOnlyList<BindingDescriptor> All { get; } = [
        I32("math.abs", args => Math.Abs(((I32Value)args[0]).Value)),
        I32("math.min", args => Math.Min(((I32Value)args[0]).Value, ((I32Value)args[1]).Value), 2),
        I32("math.max", args => Math.Max(((I32Value)args[0]).Value, ((I32Value)args[1]).Value), 2),
        new BindingDescriptor(
            "math.sqrt",
            SemVersion.One,
            [SandboxType.F64],
            SandboxType.F64,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(2),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromDouble(Math.Sqrt(((F64Value)args[0]).Value))),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.SqrtF64)))
    ];

    private static BindingDescriptor I32(string id, Func<IReadOnlyList<SandboxValue>, int> invoke, int arity = 1)
        => new(
            id,
            SemVersion.One,
            Enumerable.Repeat(SandboxType.I32, arity).ToArray(),
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(2),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromInt32(invoke(args))),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
