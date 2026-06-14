namespace DotBoxD.Hosting.Http.Internal;

using DotBoxD.Kernels;

public static class SafeHttpBindingExtensions
{
    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => DotBoxD.Hosting.Http.SafeHttpBindingExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
