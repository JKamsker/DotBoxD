namespace DotBoxD.Abstractions;

/// <summary>
/// Associates a hook <b>context</b> type with a stable hook name and exactly one result type, so
/// <c>context.Server.Hooks.On&lt;TContext&gt;()</c> can resolve the result type from the context alone
/// (rather than requiring a second generic argument at the call site). The analyzer reads this attribute
/// to validate that a <c>.Register(...)</c> / <c>.RegisterLocal(...)</c> terminal produces
/// <see cref="ResultType"/>, and persists <see cref="Name"/> as the runtime hook-point identity.
/// <para>
/// The <see cref="ResultType"/> must be a readonly partial record struct decorated with
/// <see cref="HookResultAttribute"/> that declares a <c>bool Success</c> and a <c>string? Reason</c>
/// field (see that attribute for the contract).
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Hook("combat.damage", typeof(CombatDamageResult))]
/// public sealed record CombatDamageContext(Combatant? Attacker, Combatant Victim, int Damage);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class HookAttribute : Attribute
{
    public HookAttribute(string name, Type resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resultType);

        Name = name;
        ResultType = resultType;
    }

    /// <summary>The stable hook-point name (e.g. <c>combat.damage</c>) persisted into the manifest.</summary>
    public string Name { get; }

    /// <summary>The single result type a hook on this context produces — a <see cref="HookResultAttribute"/> DTO.</summary>
    public Type ResultType { get; }
}

/// <summary>
/// Marks a readonly partial record struct as a hook <b>result</b>: a value a <c>.Register(...)</c> /
/// <c>.RegisterLocal(...)</c> terminal returns and the host applies. The DotBoxD generator emits builder members
/// the user did not
/// declare manually — <c>Ok()</c>, <c>Reject(string? reason = null)</c>, and a <c>With&lt;Field&gt;(value)</c>
/// per non-discriminator field — so result construction reads fluently and lowers to verified
/// <c>record.new</c> IR.
/// <para>
/// A hook result must be a top-level readonly partial record struct that declares a <c>bool Success</c>
/// field and a <c>string? Reason</c> field.
/// <c>Success = false</c> means "abstain, fall through to the next matching registration"; a successful
/// result may still carry a domain veto such as <c>CanDie = false</c>, so abstain and veto never overload
/// the same field.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [HookResult]
/// public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage);
///
/// // generated: CombatDamageResult.Ok().WithDamage(999), CombatDamageResult.Reject("not applicable")
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class HookResultAttribute : Attribute;

/// <summary>
/// The runtime contract every hook result satisfies so dispatch can apply the abstain/fallthrough rule
/// without reflecting on the concrete type: <c>Success == false</c> means "abstain, fall through to the next
/// matching registration". The DotBoxD generator adds this interface to every <see cref="HookResultAttribute"/>
/// record (its <c>Success</c> field implements the member), so authors never write it by hand.
/// </summary>
public interface IHookResult
{
#pragma warning disable IDE0040 // Explicit public keeps the API baseline member visible.
    public bool Success { get; }
#pragma warning restore IDE0040
}

/// <summary>
/// Marks a polymorphic host handle type whose sandbox representation is the scalar value stored in
/// the configured key member. Hook contexts may expose the handle type, while generated verified IR receives
/// only the key and calls host bindings to discriminate or query the concrete host-side entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PolymorphicHandleAttribute : Attribute
{
    public PolymorphicHandleAttribute(string keyMember)
    {
        HookContractIdentifiers.ValidateIdentifier(keyMember, nameof(keyMember));

        KeyMember = keyMember;
    }

    public string KeyMember { get; }
}

/// <summary>
/// Declares one supported subtype for a <see cref="PolymorphicHandleAttribute"/> handle. The analyzer lowers
/// <c>handle is T local</c> to <c>{BindingPrefix}.is(key)</c>, and instance host-binding calls on
/// the declared subtype receive that key as their leading sandbox argument.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class HandleSubtypeAttribute : Attribute
{
    public HandleSubtypeAttribute(
        Type subtype,
        string discriminator,
        string bindingPrefix,
        string capability)
    {
        ArgumentNullException.ThrowIfNull(subtype);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);
        HookContractIdentifiers.ValidateDottedIdentifier(bindingPrefix, nameof(bindingPrefix));
        HookContractIdentifiers.ValidateDottedIdentifier(capability, nameof(capability));

        Subtype = subtype;
        Discriminator = discriminator;
        BindingPrefix = bindingPrefix;
        Capability = capability;
    }

    public Type Subtype { get; }

    public string Discriminator { get; }

    public string BindingPrefix { get; }

    public string Capability { get; }
}

file static class HookContractIdentifiers
{
    public static void ValidateDottedIdentifier(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (!HasDottedIdentifierGrammar(value))
        {
            throw new ArgumentException("Value must be a dot-separated identifier.", paramName);
        }
    }

    public static void ValidateIdentifier(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (!HasIdentifierGrammar(value))
        {
            throw new ArgumentException("Value must be an identifier.", paramName);
        }
    }

    private static bool HasDottedIdentifierGrammar(string value)
    {
        var expectingSegmentStart = true;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '.')
            {
                if (expectingSegmentStart)
                {
                    return false;
                }

                expectingSegmentStart = true;
                continue;
            }

            if (expectingSegmentStart)
            {
                if (!IsIdentifierStart(ch))
                {
                    return false;
                }

                expectingSegmentStart = false;
                continue;
            }

            if (!IsIdentifierPart(ch))
            {
                return false;
            }
        }

        return !expectingSegmentStart;
    }

    private static bool HasIdentifierGrammar(string value)
    {
        if (!IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char ch)
        => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsIdentifierPart(char ch)
        => IsIdentifierStart(ch) || ch is >= '0' and <= '9';
}
