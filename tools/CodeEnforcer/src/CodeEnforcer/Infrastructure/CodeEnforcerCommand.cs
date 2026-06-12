using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeEnforcer;

internal sealed class CodeEnforcerCommand : Command<CodeEnforcerSettings>
{
    private readonly IAnsiConsole output;
    private readonly IAnsiConsole error;

    public CodeEnforcerCommand()
        : this(AnsiConsole.Console, CreateErrorConsole())
    {
    }

    internal CodeEnforcerCommand(IAnsiConsole output, IAnsiConsole error)
    {
        this.output = output;
        this.error = error;
    }

    protected override int Execute(
        CommandContext context,
        CodeEnforcerSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            CodeEnforcerConfig config = CodeEnforcerConfig.Load(
                Environment.CurrentDirectory,
                settings.ConfigPath);
            string root = settings.RootDirectory ?? RepositoryPaths.DiscoverRoot(config.ConfigDirectory);
            settings.ApplyOverrides(config);

            CodebaseSnapshot snapshot = CodeFileCollector.Collect(root);
            IReadOnlyList<CodeViolation> violations = new CodeEnforcerEngine().Check(snapshot, config);
            WriteResult(violations);
            return violations.Count == 0 ? ExitCodes.Success : ExitCodes.ViolationsFound;
        }
        catch (CodeEnforcerException ex)
        {
            WriteError(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.InternalError;
        }
    }

    private void WriteResult(IReadOnlyList<CodeViolation> violations)
    {
        foreach (CodeViolation violation in violations)
        {
            output.MarkupLine(
                $"[red]{Markup.Escape(violation.Rule)}[/] {Markup.Escape(violation.Path)}: {Markup.Escape(violation.Message)}");
        }

        if (violations.Count == 0)
        {
            output.MarkupLine("[green]CodeEnforcer passed.[/]");
            return;
        }

        output.MarkupLine(
            $"[red]CodeEnforcer found {violations.Count.ToStringInvariant()} violation(s).[/]");
    }

    private void WriteError(string message) =>
        error.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");

    private static IAnsiConsole CreateErrorConsole() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });
}
