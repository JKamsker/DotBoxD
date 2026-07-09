using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class DefaultSandboxBindings
{
    public static BindingRegistryBuilder AddDefaultPureBindings(this BindingRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddRange(MathBindings.All).AddRange(StringBindings.All);
    }

    public static BindingRegistryBuilder AddFileBindings(this BindingRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(SafeFileBindings.ReadText).Add(SafeFileBindings.WriteText);
    }

    public static BindingRegistryBuilder AddTimeBindings(this BindingRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(SafeTimeBindings.NowUnixMillis);
    }

    public static BindingRegistryBuilder AddRandomBindings(this BindingRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(SafeRandomBindings.NextI32);
    }

    public static BindingRegistryBuilder AddLogBindings(this BindingRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(SafeLogBindings.Info).Add(SafeLogBindings.Warn);
    }
}
