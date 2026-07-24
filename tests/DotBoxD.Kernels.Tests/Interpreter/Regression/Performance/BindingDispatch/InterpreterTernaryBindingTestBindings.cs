using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.BindingDispatch;

internal sealed class FastTernaryBinding(Action? onInvoke = null) : IThreeArgumentBindingInvoker
{
    internal int FastCalls { get; private set; }
    internal int ListCalls { get; private set; }

    internal BindingDescriptor Descriptor(string id = "test.ternary")
        => InterpreterScalarBindingDescriptor.Create(
            id,
            [SandboxType.I32, SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            Invoke);

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        ListCalls++;
        onInvoke?.Invoke();
        return TernaryBindingValue.Combine(args[0], args[1], args[2]);
    }

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2,
        CancellationToken cancellationToken)
    {
        FastCalls++;
        onInvoke?.Invoke();
        return TernaryBindingValue.Combine(arg0, arg1, arg2);
    }
}

internal sealed class RegularTernaryBinding
{
    private readonly List<IReadOnlyList<SandboxValue>> _arguments = [];

    internal int Calls { get; private set; }
    internal IReadOnlyList<IReadOnlyList<SandboxValue>> Arguments => _arguments;

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            "test.ternary",
            [SandboxType.I32, SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            Invoke);

    private ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        Calls++;
        _arguments.Add(args);
        return TernaryBindingValue.Combine(args[0], args[1], args[2]);
    }
}

internal static class TernaryBindingValue
{
    internal static ValueTask<SandboxValue> Combine(
        SandboxValue first,
        SandboxValue second,
        SandboxValue third)
        => ValueTask.FromResult(SandboxValue.FromInt32(
            ((I32Value)first).Value * 100 +
            ((I32Value)second).Value * 10 +
            ((I32Value)third).Value));
}
