namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    // A compiled entrypoint is synchronous: generated code publishes immediately before returning, and the
    // host consumes on the same call stack. Thread-local storage avoids growing every SandboxContext (including
    // interpreted and scalar-only runs). Context and value identity make a miss fail closed under custom
    // compilers, thread migration, or reentrant execution.
    [ThreadStatic]
    private static CompiledReturnValidationSlot? t_returnValidation;

    internal void ResetForCompiledNoAuditReuse()
    {
        ClearCompiledReturnValidation();
        Interlocked.Exchange(ref _sharedWallTimeToken, null)?.DisposeOwned();
        _deterministicRandom = null;
        _returnCredits = null;
        _bindingGrantClock = null;
        _lastCapabilityId = null;
        _lastCapabilityClock = default;
        _lastCapabilityGrant = null;
        _lastBindingId = null;
        _lastBindingDescriptor = null;
        _callDepth = 0;
    }

    internal void RecordCompiledReturnValidation(SandboxValue value, SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedType);
        if (_callDepth > 1)
        {
            // Recursive calls to the selected entrypoint still validate their own return, but only the
            // outermost return can prove the value that crosses the host boundary.
            return;
        }

        if (t_returnValidation is { } slot)
        {
            slot.Set(this, value, expectedType);
            return;
        }

        t_returnValidation = new CompiledReturnValidationSlot(this, value, expectedType);
    }

    internal bool TryConsumeCompiledReturnValidation(
        SandboxValue value,
        SandboxType expectedType)
    {
        return t_returnValidation?.TryConsume(this, value, expectedType) == true;
    }

    internal void ClearCompiledReturnValidation()
    {
        t_returnValidation?.Clear(this);
    }

    private sealed class CompiledReturnValidationSlot(
        SandboxContext context,
        SandboxValue value,
        SandboxType expectedType)
    {
        private readonly WeakReference<SandboxContext> _context = new(context);
        private readonly WeakReference<SandboxValue> _value = new(value);
        private readonly WeakReference<SandboxType> _type = new(expectedType);

        public void Set(SandboxContext owner, SandboxValue result, SandboxType type)
        {
            _value.SetTarget(result);
            _type.SetTarget(type);
            _context.SetTarget(owner);
        }

        public bool TryConsume(SandboxContext owner, SandboxValue result, SandboxType type)
        {
            if (!_context.TryGetTarget(out var recordedContext) ||
                !ReferenceEquals(recordedContext, owner))
            {
                return false;
            }

            var matches = _value.TryGetTarget(out var recordedValue) &&
                          ReferenceEquals(recordedValue, result) &&
                          _type.TryGetTarget(out var recordedType) &&
                          recordedType.Equals(type);
            ClearTargets();
            return matches;
        }

        public void Clear(SandboxContext owner)
        {
            if (_context.TryGetTarget(out var recordedContext) &&
                ReferenceEquals(recordedContext, owner))
            {
                ClearTargets();
            }
        }

        private void ClearTargets()
        {
            _context.SetTarget(null!);
            _value.SetTarget(null!);
            _type.SetTarget(null!);
        }
    }
}
