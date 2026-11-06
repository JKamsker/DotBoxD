using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Compiler.Emitters;

using DotBoxD.Kernels;

/// <summary>
/// The binding catalog as seen from inside one module. Validation resolves a call to a module function
/// before a binding with the same id, so a binding shadowed by a module function is never the call
/// target there. Hiding it keeps every compiled intrinsic and loop fast path that matches a binding by
/// call name from replacing the module function, mirroring the interpreter.
/// </summary>
internal sealed class ModuleScopedBindingCatalog : IBindingCatalog
{
    private readonly IBindingCatalog _inner;
    private readonly HashSet<string> _moduleFunctions;
    private IReadOnlyList<BindingSignature>? _signatures;

    public ModuleScopedBindingCatalog(IBindingCatalog inner, IEnumerable<SandboxFunction> moduleFunctions)
    {
        _inner = inner;
        _moduleFunctions = new HashSet<string>(moduleFunctions.Select(f => f.Id), StringComparer.Ordinal);
    }

    public IReadOnlyList<BindingSignature> Signatures
        => _signatures ??= _inner.Signatures.Where(s => !_moduleFunctions.Contains(s.Id)).ToArray();

    public string ManifestHash => _inner.ManifestHash;

    public bool TryGet(string id, out BindingSignature binding)
    {
        if (_moduleFunctions.Contains(id))
        {
            binding = null!;
            return false;
        }

        return _inner.TryGet(id, out binding);
    }

    public bool Contains(string id)
        => !_moduleFunctions.Contains(id) && _inner.Contains(id);

    public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        => _inner.TryGetCapabilityGrantValidator(capabilityId, out validator);
}
