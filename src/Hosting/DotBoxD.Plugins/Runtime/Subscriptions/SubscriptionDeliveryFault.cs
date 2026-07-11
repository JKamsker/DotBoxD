namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Which stage of subscription delivery threw. <see cref="Filter"/> is a lowered server-side
/// <c>Where</c>/projection predicate; <see cref="Handler"/> is a terminal handler (including a <c>RunLocal</c>
/// projection push and the native decode/dispatch it drives across the host boundary).
/// </summary>
public enum SubscriptionDeliveryStage
{
    Filter,
    Handler,
}

/// <summary>
/// A fault caught while delivering a published event to a subscription. Delivery runs on a background task and
/// these faults are otherwise swallowed (so the game loop is never blocked or crashed by plugin code); the host
/// observes them via <c>PluginServer.Create(onSubscriptionFault: ...)</c> to surface the failure in its log,
/// so a misbehaving <c>Subscriptions.On&lt;T&gt;().Where(...).RunLocal(...)</c> chain is diagnosable instead of
/// silently doing nothing.
/// </summary>
/// <param name="EventType">The event type whose delivery faulted.</param>
/// <param name="Stage">The delivery stage that threw.</param>
/// <param name="Exception">The exception that was caught.</param>
public sealed record SubscriptionDeliveryFault(
    Type EventType,
    SubscriptionDeliveryStage Stage,
    Exception Exception)
{
    private Type _eventType = EventType ?? throw new ArgumentNullException(nameof(EventType));
    private SubscriptionDeliveryStage _stage = Defined(Stage);
    private Exception _exception = Exception ?? throw new ArgumentNullException(nameof(Exception));

    public Type EventType
    {
        get => _eventType;
        init => _eventType = value ?? throw new ArgumentNullException(nameof(EventType));
    }

    public SubscriptionDeliveryStage Stage
    {
        get => _stage;
        init => _stage = Defined(value);
    }

    public Exception Exception
    {
        get => _exception;
        init => _exception = value ?? throw new ArgumentNullException(nameof(Exception));
    }

    private static SubscriptionDeliveryStage Defined(SubscriptionDeliveryStage value)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(Stage));
}
