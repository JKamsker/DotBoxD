namespace DotBoxD.Plugins.Runtime;

internal static class TypedRemoteContextInvoker
{
    public static ValueTask Invoke<TValue, TContext>(
        TValue value,
        HookContext rawContext,
        Func<HookContext, TContext> createContext,
        Func<TValue, TContext, ValueTask> handler)
    {
        var context = createContext(rawContext);
        rawContext.CancellationToken.ThrowIfCancellationRequested();
        return handler(value, context);
    }
}
