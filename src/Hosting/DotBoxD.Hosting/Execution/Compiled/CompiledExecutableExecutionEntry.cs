namespace DotBoxD.Hosting.Execution.Compiled;

internal readonly record struct CompiledExecutableExecutionKey(
    string PlanHash,
    string Entrypoint);

internal sealed class CompiledExecutableExecutionEntry(
    CompiledExecutableExecutionKey key,
    ExecutionPlan plan,
    Lazy<Task<CompiledExecutable>> executable)
{
    private CompiledExecutablePublication? _publication;

    public CompiledExecutableExecutionKey Key { get; } = key;
    public ExecutionPlan Plan { get; } = plan;
    public Lazy<Task<CompiledExecutable>>? Executable { get; private set; } = executable;
    public CompiledExecutable? Completed { get; private set; }

    public bool Matches(ExecutionPlan plan, string entrypoint)
        => ReferenceEquals(Plan, plan) &&
           StringComparer.Ordinal.Equals(Key.Entrypoint, entrypoint);

    public void MarkCompleted(CompiledExecutable executable)
    {
        if (Completed is null)
        {
            Completed = executable;
        }
    }

    public CompiledExecutablePublication GetOrCreatePublication()
    {
        var completed = Completed ?? throw new InvalidOperationException(
            "A compiled executable cannot be published before materialization completes.");
        return _publication ??= new CompiledExecutablePublication(this, completed);
    }

    public void Invalidate()
    {
        _publication = null;
        Completed = null;
        Executable = null;
    }
}
