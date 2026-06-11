namespace SafeIR.Runtime;

using SafeIR;

public static class DefaultSandboxBindings
{
    public static BindingRegistryBuilder AddDefaultPureBindings(this BindingRegistryBuilder builder)
        => builder.AddRange(MathBindings.All).AddRange(StringBindings.All);

    public static BindingRegistryBuilder AddFileBindings(this BindingRegistryBuilder builder)
        => builder.Add(SafeFileBindings.ReadText);
}
