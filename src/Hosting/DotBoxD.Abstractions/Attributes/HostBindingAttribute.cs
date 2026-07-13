using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Marks a host-service method as a sandbox binding the DotBoxD.Kernels generator may call from verified IR.
/// A kernel reaches the service through <see cref="HookContext.Host{THost}"/> or a constructor-injected
/// service field; the generator lowers that call to a <c>CallExpression(bindingId, …)</c>, records the
/// capability and effects, and expects the host to register a matching binding. Set <see cref="IsAsync"/>
/// when that registered binding declares asynchronous host work.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class HostBindingAttribute : Attribute
{
    public HostBindingAttribute(string bindingId, string capability, SandboxEffect effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        BindingId = bindingId;
        Capability = capability;
        Effects = effects;
    }

    public HostBindingAttribute(string capability, SandboxEffect effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        BindingId = string.Empty;
        Capability = capability;
        Effects = effects;
    }

    /// <summary>The sandbox binding id the call lowers to (e.g. <c>host.world.getHealth</c>).</summary>
    public string BindingId { get; }

    /// <summary>The capability the call requires (e.g. <c>game.world.monster.read.health</c>).</summary>
    public string Capability { get; }

    /// <summary>
    /// The sandbox effects the binding declares — must equal the registered binding's effects so the
    /// manifest's effects match the verified entrypoint effects (a read is
    /// <c>SandboxEffect.Cpu | SandboxEffect.HostStateRead</c>).
    /// </summary>
    public SandboxEffect Effects { get; }

    /// <summary>True when the binding id is derived from the annotated service method.</summary>
    public bool IsAutoBinding => BindingId.Length == 0;

    /// <summary>
    /// True when the registered sandbox binding uses asynchronous host work even if its declared
    /// effects are otherwise ordinary read/write effects.
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// Includes the instance expression as argument zero when lowering an explicitly identified binding.
    /// This lets SDK value objects expose host-backed convenience methods while keeping the registered
    /// binding signature explicit: <c>value.Check(arg)</c> binds as <c>Check(value, arg)</c>.
    /// </summary>
    public bool IncludeReceiver { get; set; }
}
