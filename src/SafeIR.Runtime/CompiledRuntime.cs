namespace SafeIR.Runtime;

using SafeIR;

public static class CompiledRuntime
{
    public static void ChargeFuel(SandboxContext context, int amount) => context.ChargeFuel(amount);

    public static void EnterCall(SandboxContext context) => context.EnterCall();

    public static void ExitCall(SandboxContext context) => context.ExitCall();

    public static SandboxValue GetInputArgument(SandboxValue input, int index)
    {
        if (index == 0 && input is not ListValue) {
            return input;
        }

        if (input is ListValue list && index >= 0 && index < list.Values.Count) {
            return list.Values[index];
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.InvalidInput, "entrypoint input argument mismatch"));
    }

    public static SandboxValue I32(int value) => SandboxValue.FromInt32(value);

    public static SandboxValue Bool(bool value) => SandboxValue.FromBool(value);

    private static SandboxValue String(string value) => SandboxValue.FromString(value);

    public static SandboxValue StringConst(SandboxContext context, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromString(value);
    }

    public static int AsI32(SandboxValue value) => ((I32Value)value).Value;

    public static bool AsBool(SandboxValue value) => ((BoolValue)value).Value;

    public static double AsF64(SandboxValue value) => ((F64Value)value).Value;

    public static SandboxValue AddI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) + AsI32(right));

    public static SandboxValue SubI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) - AsI32(right));

    public static SandboxValue MulI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) * AsI32(right));

    public static SandboxValue DivI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) / AsI32(right));

    public static SandboxValue RemI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) % AsI32(right));

    public static SandboxValue NegI32(SandboxValue value) => I32(-AsI32(value));

    public static SandboxValue NotBool(SandboxValue value) => Bool(!AsBool(value));

    public static SandboxValue Eq(SandboxValue left, SandboxValue right) => Bool(Equals(left, right));

    public static SandboxValue Ne(SandboxValue left, SandboxValue right) => Bool(!Equals(left, right));

    public static SandboxValue LtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) < AsI32(right));

    public static SandboxValue LteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) <= AsI32(right));

    public static SandboxValue GtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) > AsI32(right));

    public static SandboxValue GteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) >= AsI32(right));

    public static SandboxValue And(SandboxValue left, SandboxValue right) => Bool(AsBool(left) && AsBool(right));

    public static SandboxValue Or(SandboxValue left, SandboxValue right) => Bool(AsBool(left) || AsBool(right));

    public static SandboxValue StringLength(SandboxValue value) => I32(((StringValue)value).Value.Length);

    public static SandboxValue ConcatString(SandboxContext context, SandboxValue left, SandboxValue right)
    {
        var text = ((StringValue)left).Value + ((StringValue)right).Value;
        context.ChargeString(text);
        return String(text);
    }

    public static SandboxValue SqrtF64(SandboxValue value) => SandboxValue.FromDouble(Math.Sqrt(AsF64(value)));

    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        context.ChargeBindingCall(descriptor);
        return descriptor.Interpreter(context, args, context.CancellationToken).AsTask().GetAwaiter().GetResult();
    }
}
