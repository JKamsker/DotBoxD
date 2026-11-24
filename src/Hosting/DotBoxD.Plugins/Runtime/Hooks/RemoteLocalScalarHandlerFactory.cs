namespace DotBoxD.Plugins.Runtime.Hooks;

using System.Runtime.CompilerServices;

internal static class RemoteLocalScalarHandlerFactory
{
    public static bool TryCreate<TProjected>(
        Func<TProjected, HookContext, ValueTask> handler,
        out Func<KernelRpcValue, HookContext, CancellationToken, ValueTask> invoke)
    {
        if (typeof(TProjected) == typeof(int))
        {
            var typed = (Func<int, HookContext, ValueTask>)(object)handler;
            invoke = (value, context, cancellationToken) =>
                Invoke(typed, value.Int32Value, context, cancellationToken);
            return true;
        }

        if (typeof(TProjected).IsEnum &&
            Enum.GetUnderlyingType(typeof(TProjected)) == typeof(int))
        {
            invoke = (value, context, cancellationToken) =>
                Invoke(handler, FromInt32Enum<TProjected>(value.Int32Value), context, cancellationToken);
            return true;
        }

        invoke = null!;
        return false;
    }

    private static TEnum FromInt32Enum<TEnum>(int value)
        => Unsafe.As<int, TEnum>(ref value);

    private static ValueTask Invoke<TProjected>(
        Func<TProjected, HookContext, ValueTask> handler,
        TProjected value,
        HookContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();
        return handler(value, context);
    }
}
