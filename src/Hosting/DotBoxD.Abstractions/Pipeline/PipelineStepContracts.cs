namespace DotBoxD.Abstractions;

/// <summary>
/// Whether a pipeline surface runs entirely in-host or ships its lowered kernel across the remote transport.
/// </summary>
public enum PipelineTransport
{
    /// <summary>An in-host pipeline (e.g. <c>HookPipeline</c>, <c>SubscriptionPipeline</c>).</summary>
    Local = 0,

    /// <summary>A pipeline whose lowered kernel is shipped to and verified by the remote host
    /// (e.g. <c>RemoteHookPipeline</c>, <c>RemoteSubscriptionPipeline</c>).</summary>
    Remote = 1,
}

/// <summary>
/// Marks a fluent pipeline/stage type as a recognized event-pipeline surface, replacing the generator's
/// hardcoded receiver-type allow-list. Stage and terminal calls on a marked surface expose explicit IR
/// companion parameters; the source generator supplies those companions through interceptors.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class PipelineSurfaceAttribute : Attribute
{
    public PipelineSurfaceAttribute(PipelineTransport transport)
    {
        if (transport is not (PipelineTransport.Local or PipelineTransport.Remote))
        {
            throw new ArgumentOutOfRangeException(nameof(transport));
        }

        Transport = transport;
    }

    /// <summary>Whether chains rooted on this surface run in-host or ship to the remote host.</summary>
    public PipelineTransport Transport { get; }
}
