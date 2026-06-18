namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>
/// Marks an event property the host keeps a dispatch index for. It is a <b>host abstraction</b>, not a
/// DotBoxD framework concept: DotBoxD owns predicate lowering and exposes index metadata on the plugin
/// manifest (<c>HookSubscriptionManifest.IndexedPredicates</c>); this attribute is how <i>this</i> host
/// declares which of those property paths it can actually serve from an equality/range bucket. The host
/// reads it at registration (see <c>EventIndexMatcher</c>) and ignores manifest predicates whose path is
/// not an index key, leaving them to the verified IR.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class EventIndexKeyAttribute : Attribute;
