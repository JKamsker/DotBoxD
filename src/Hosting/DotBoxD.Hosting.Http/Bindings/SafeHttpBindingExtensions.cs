using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Http.Bindings;

public static class SafeHttpBindingExtensions
{
    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(SafeHttpBindings.GetText(invoker, dnsResolver));
    }
}
