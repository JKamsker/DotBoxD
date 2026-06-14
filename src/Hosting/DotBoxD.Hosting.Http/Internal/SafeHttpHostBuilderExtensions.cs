namespace DotBoxD.Hosting.Http.Internal;

public static class SafeHttpHostBuilderExtensions
{
    public static SandboxHostBuilder AddNetworkBindings(
        this SandboxHostBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => DotBoxD.Hosting.Http.SafeHttpHostBuilderExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
