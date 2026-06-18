using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDManifestEffectModel
{
    private static readonly EquatableArray<string> NonAllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.HostStateWrite,
            DotBoxDGenerationNames.Effects.Concurrency,
            DotBoxDGenerationNames.Effects.Audit
        });

    private static readonly EquatableArray<string> AllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.Alloc,
            DotBoxDGenerationNames.Effects.HostStateWrite,
            DotBoxDGenerationNames.Effects.Concurrency,
            DotBoxDGenerationNames.Effects.Audit
        });

    // A value-returning projection terminal (a lowered RunLocal) performs NO host send: it only computes and
    // returns a value, so its verified effects are just Cpu (plus Alloc when the projection allocates) — never
    // the send-specific HostStateWrite/Concurrency/Audit. The manifest must match the verified entrypoint
    // effects EXACTLY (DBXK041), so these sets omit them; host-binding effects still ride in via extraEffects.
    private static readonly EquatableArray<string> ProjectionEffects =
        EquatableArray<string>.FromOwned(new[] { DotBoxDGenerationNames.Effects.Cpu });

    private static readonly EquatableArray<string> AllocatingProjectionEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.Alloc
        });

    public static EquatableArray<string> Create(
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDHandleModel handle,
        ICollection<string>? extraEffects = null)
        => Append(
            shouldHandle.Allocates || handle.Allocates ? AllocatingEffects : NonAllocatingEffects,
            extraEffects);

    /// <summary>
    /// Effect set for a chain whose terminal is a value-returning projection (a lowered <c>RunLocal</c>):
    /// Cpu (plus Alloc when allocating) and any host-binding effects, with no send-specific effects.
    /// </summary>
    public static EquatableArray<string> Create(
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string>? extraEffects = null)
        => Append(
            shouldHandle.Allocates || handleBody.Allocates ? AllocatingProjectionEffects : ProjectionEffects,
            extraEffects);

    private static EquatableArray<string> Append(
        EquatableArray<string> baseEffects,
        ICollection<string>? extraEffects)
    {
        if (extraEffects is null || extraEffects.Count == 0)
        {
            return baseEffects;
        }

        // Preserve the base order, then append the host-binding effects (deterministically ordered) the
        // base does not already declare — so a HostStateRead binding adds "HostStateRead" to the manifest.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(baseEffects.Count + extraEffects.Count);
        foreach (var effect in baseEffects)
        {
            if (seen.Add(effect))
            {
                result.Add(effect);
            }
        }

        foreach (var effect in extraEffects)
        {
            if (seen.Add(effect))
            {
                result.Add(effect);
            }
        }

        return EquatableArray<string>.FromOwned([.. result]);
    }
}
