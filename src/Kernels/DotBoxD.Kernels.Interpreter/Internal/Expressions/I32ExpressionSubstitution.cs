namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

internal readonly struct I32ExpressionSubstitution
{
    // Inline eligibility currently admits exactly one parameter and forbids nested calls. If that
    // contract broadens, this representation must broaden with it rather than dropping substitutions.
    private readonly string? _name;
    private readonly I32ExpressionPlan? _plan;

    public I32ExpressionSubstitution(string name, I32ExpressionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(plan);
        _name = name;
        _plan = plan;
    }

    public bool IsPresent => _plan is not null;

    public bool TryGetValue(string name, out I32ExpressionPlan plan)
    {
        if (_plan is not null && string.Equals(_name, name, StringComparison.Ordinal))
        {
            plan = _plan;
            return true;
        }

        plan = null!;
        return false;
    }
}
