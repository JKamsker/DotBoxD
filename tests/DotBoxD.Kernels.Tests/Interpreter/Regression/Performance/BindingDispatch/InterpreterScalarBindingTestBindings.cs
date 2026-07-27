using System.Collections.Concurrent;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.BindingDispatch;

internal static class InterpreterScalarBindingDescriptor
{
    internal static BindingDescriptor Create(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        BindingInvoker invoke,
        bool isAsync = false)
        => new(
            id,
            SemVersion.One,
            parameters,
            returnType,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(7),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = isAsync
        };
}

internal sealed class FastUnaryBinding : IOneArgumentBindingInvoker
{
    internal int FastCalls { get; private set; }
    internal int ListCalls { get; private set; }

    internal BindingDescriptor Descriptor(string id = "test.unary")
        => InterpreterScalarBindingDescriptor.Create(
            id,
            [SandboxType.I32],
            SandboxType.I32,
            Invoke);

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        ListCalls++;
        return ValueTask.FromResult(args[0]);
    }

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        CancellationToken cancellationToken)
    {
        FastCalls++;
        return ValueTask.FromResult(arg0);
    }
}

internal sealed class RegularUnaryBinding
{
    internal int Calls { get; private set; }

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            "test.unary",
            [SandboxType.I32],
            SandboxType.I32,
            Invoke);

    private ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(args[0]);
    }
}

internal sealed class FastBinaryBinding(Action? onInvoke = null) : ITwoArgumentBindingInvoker
{
    internal int FastCalls { get; private set; }
    internal int ListCalls { get; private set; }

    internal BindingDescriptor Descriptor(string id = "test.binary")
        => InterpreterScalarBindingDescriptor.Create(
            id,
            [SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            Invoke);

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        ListCalls++;
        onInvoke?.Invoke();
        return Combine(args[0], args[1]);
    }

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        SandboxValue arg1,
        CancellationToken cancellationToken)
    {
        FastCalls++;
        onInvoke?.Invoke();
        return Combine(arg0, arg1);
    }

    private static ValueTask<SandboxValue> Combine(SandboxValue arg0, SandboxValue arg1)
    {
        var left = ((I32Value)arg0).Value;
        var right = ((I32Value)arg1).Value;
        return ValueTask.FromResult(SandboxValue.FromInt32((left * 10) + right));
    }
}

internal sealed class RegularBinaryBinding
{
    internal int Calls { get; private set; }

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            "test.binary",
            [SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            Invoke);

    private ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        Calls++;
        var left = ((I32Value)args[0]).Value;
        var right = ((I32Value)args[1]).Value;
        return ValueTask.FromResult(SandboxValue.FromInt32((left * 10) + right));
    }
}

internal sealed class RetainingBinaryBinding
{
    private readonly List<IReadOnlyList<SandboxValue>> _arguments = [];

    internal IReadOnlyList<IReadOnlyList<SandboxValue>> Arguments => _arguments;

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            "test.retain",
            [SandboxType.I32, SandboxType.I32],
            SandboxType.Unit,
            Invoke);

    private ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        _arguments.Add(args);
        return ValueTask.FromResult(SandboxValue.Unit);
    }
}

internal sealed class OrderedValueBinding(
    string id,
    int value,
    string eventName,
    ConcurrentQueue<string> events,
    bool pending)
{
    private readonly TaskCompletionSource<SandboxValue>? _completion = pending
        ? new(TaskCreationOptions.RunContinuationsAsynchronously)
        : null;
    private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Invoked => _invoked.Task;

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            id,
            [],
            SandboxType.I32,
            Invoke,
            isAsync: pending);

    internal void Complete()
        => _completion?.TrySetResult(SandboxValue.FromInt32(value));

    private ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        events.Enqueue(eventName);
        if (_completion is null)
        {
            return ValueTask.FromResult(SandboxValue.FromInt32(value));
        }

        _invoked.TrySetResult();
        return new ValueTask<SandboxValue>(_completion.Task);
    }
}

internal sealed class ControlledFastUnaryBinding(bool isAsync) : IOneArgumentBindingInvoker
{
    private readonly TaskCompletionSource<SandboxValue> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invoked =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int FastCalls { get; private set; }
    internal int ListCalls { get; private set; }
    internal Task Invoked => _invoked.Task;

    internal BindingDescriptor Descriptor()
        => InterpreterScalarBindingDescriptor.Create(
            "test.pendingUnary",
            [SandboxType.I32],
            SandboxType.I32,
            Invoke,
            isAsync);

    internal void Complete(string completion)
    {
        switch (completion)
        {
            case "success":
                _completion.TrySetResult(SandboxValue.FromInt32(42));
                break;
            case "faulted":
                _completion.TrySetException(new InvalidOperationException("secret binding failure"));
                break;
            case "canceled":
                _completion.TrySetCanceled(new CancellationToken(canceled: true));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(completion), completion, "unknown completion");
        }
    }

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken)
    {
        ListCalls++;
        _invoked.TrySetResult();
        return new ValueTask<SandboxValue>(_completion.Task);
    }

    public ValueTask<SandboxValue> Invoke(
        SandboxContext context,
        SandboxValue arg0,
        CancellationToken cancellationToken)
    {
        FastCalls++;
        _invoked.TrySetResult();
        return new ValueTask<SandboxValue>(_completion.Task);
    }
}
