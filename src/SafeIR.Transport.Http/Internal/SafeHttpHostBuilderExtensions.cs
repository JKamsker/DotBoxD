namespace SafeIR.Transport.Http.Internal;

using SafeIR.Runtime;

public static class SafeHttpHostBuilderExtensions
{
    public static SandboxHostBuilder AddNetworkBindings(
        this SandboxHostBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddBinding(SafeHttpBindings.GetText(invoker, dnsResolver));
        return builder;
    }
}
