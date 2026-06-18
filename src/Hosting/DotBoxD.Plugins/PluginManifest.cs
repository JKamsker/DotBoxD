using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed record PluginManifest(
    string PluginId,
    string Contract,
    ExecutionMode Mode,
    IReadOnlyList<string> Effects,
    IReadOnlyList<LiveSettingDefinition> LiveSettings,
    IReadOnlyList<HookSubscriptionManifest> Subscriptions)
{
    private IReadOnlyList<string> _effects = PluginModelCopy.List(Effects);
    private IReadOnlyList<LiveSettingDefinition> _liveSettings = PluginModelCopy.List(LiveSettings);
    private IReadOnlyList<HookSubscriptionManifest> _subscriptions = PluginModelCopy.List(Subscriptions);
    private IReadOnlyList<string> _requiredCapabilities = [];

    public IReadOnlyList<string> Effects { get => _effects; init => _effects = PluginModelCopy.List(value); }
    public IReadOnlyList<LiveSettingDefinition> LiveSettings { get => _liveSettings; init => _liveSettings = PluginModelCopy.List(value); }
    public IReadOnlyList<HookSubscriptionManifest> Subscriptions { get => _subscriptions; init => _subscriptions = PluginModelCopy.List(value); }

    /// <summary>
    /// Capabilities the verified IR requires — derived by the analyzer from what the kernel actually
    /// touches (a host-message send, a <c>[HostBinding]</c> call, a <c>[Capability]</c>-gated event
    /// property), never self-asserted for trust. Declarative: the host gates installs through binding
    /// capabilities and policy grants. Optional, defaults to empty; set via object initializer.
    /// </summary>
    public IReadOnlyList<string> RequiredCapabilities
    {
        get => _requiredCapabilities;
        init => _requiredCapabilities = PluginModelCopy.List(value ?? []);
    }

    /// <summary>
    /// For a <b>server extension</b> kernel: the id of the single request/response entrypoint function
    /// the host invokes with caller arguments and whose result it returns (see
    /// <c>InstalledKernel.InvokeServerExtensionAsync</c>). <c>null</c> for ordinary event kernels, which dispatch
    /// through <c>ShouldHandle</c>/<c>Handle</c> instead. Additive; defaults to <c>null</c>.
    /// </summary>
    public string? RpcEntrypoint { get; init; }
}

public sealed record LiveSettingDefinition(
    string Name,
    string Type,
    object? DefaultValue,
    object? Min = null,
    object? Max = null);

public sealed record HookSubscriptionManifest(string Event, string Kernel)
{
    private IReadOnlyList<IndexedPredicate> _indexedPredicates = [];

    /// <summary>
    /// Host-readable, index-eligible constraints DotBoxD extracted from the lowered <c>.Where(...)</c>
    /// chain — each a single <c>event-property &lt;op&gt; constant</c> comparison that the host may compile
    /// into an equality/range dispatch bucket to prefilter events <i>before</i> the verified IR predicate
    /// runs. Always a subset of the real predicate (every entry is a necessary AND condition), so rejecting
    /// on any entry is safe regardless of <see cref="IndexCoversPredicate"/>. Optional; empty when the
    /// predicate had no index-eligible leaves. Set via object initializer.
    /// </summary>
    public IReadOnlyList<IndexedPredicate> IndexedPredicates
    {
        get => _indexedPredicates;
        init => _indexedPredicates = PluginModelCopy.List(value ?? []);
    }

    /// <summary>
    /// <c>true</c> only when the entire lowered predicate is exactly the conjunction of
    /// <see cref="IndexedPredicates"/> — i.e. the index fully covers it, so a host whose index check passes
    /// MAY skip the verified IR. <c>false</c> (the conservative default) means the index is a prefilter
    /// only and the verified IR predicate must still run after the host's index check accepts an event.
    /// </summary>
    public bool IndexCoversPredicate { get; init; }
}

/// <summary>
/// One index-eligible predicate from a lowered <c>.Where(...)</c> chain: a comparison between an event
/// property (<see cref="Path"/>) and a compile-time constant (<see cref="Value"/>). Hosts match
/// <see cref="Path"/> against their own indexed fields and dispatch through equality/range buckets.
/// </summary>
public sealed record IndexedPredicate(
    string Path,
    IndexPredicateOperator Operator,
    object? Value,
    string ValueType);

/// <summary>
/// The comparison an <see cref="IndexedPredicate"/> applies, normalized so the event property is always the
/// left operand (<c>e.Damage &gt;= 5</c> and <c>5 &lt;= e.Damage</c> both lower to
/// <see cref="GreaterThanOrEqual"/>).
/// </summary>
public enum IndexPredicateOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

public sealed record KernelEntrypoints(string ShouldHandle, string Handle);

public sealed record PluginPackage(
    PluginManifest Manifest,
    SandboxModule Module,
    KernelEntrypoints Entrypoints)
{
    public static PluginPackage Create(
        PluginManifest manifest,
        SandboxModule module,
        KernelEntrypoints? entrypoints = null)
        => new(
            manifest,
            module,
            entrypoints ?? new KernelEntrypoints(
                PluginManifestNames.Entrypoints.ShouldHandle,
                PluginManifestNames.Entrypoints.Handle));
}
