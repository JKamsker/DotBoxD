using System.Text.Json;
using DotBoxD.Cli.Commands;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Cli.Infrastructure;

public static class CommandLine
{
    private const string Help = """
        DotBoxD local inspection tools
        Usage:
          dotboxd explain <module.json|package.json|execution.dbxtrace> [--json]
          dotboxd replay <execution.dbxtrace> [--backend interpreter|compiled|auto] [--json]
          dotboxd --help
          dotboxd --version

        Explain validates and displays lowered IR, capabilities and bindings without executing it.
        Replay runs recorded IR offline; it never loads plugin assemblies or calls live bindings.
        Input files are explicit. Commands never prompt, read stdin, or modify input files.
        --json selects the version 1 output envelope. Default output is human-readable.
        Exit codes: 0 success/matching replay; 1 invalid execution or divergence; 2 usage/input error.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"] ||
                args is ["replay", "--help"] or ["explain", "--help"])
            {
                await output.WriteLineAsync(Help).ConfigureAwait(false);
                return 0;
            }
            if (args is ["--version"])
            {
                await output.WriteLineAsync(typeof(CommandLine).Assembly.GetName().Version!.ToString()).ConfigureAwait(false);
                return 0;
            }
            var arguments = args.Where(argument => argument != "--json").ToArray();
            if (args.Count(argument => argument == "--json") > 1 || arguments.Length < 2)
            {
                throw new ArgumentException("Supply exactly one command and input file. Run dotboxd --help.");
            }
            var mode = ExecutionMode.Interpreted;
            if (arguments.Length == 4 && arguments[0] == "replay" && arguments[2] == "--backend")
            {
                mode = arguments[3] switch
                {
                    "interpreter" => ExecutionMode.Interpreted,
                    "compiled" => ExecutionMode.Compiled,
                    "auto" => ExecutionMode.Auto,
                    _ => throw new ArgumentException("Backend must be interpreter, compiled, or auto.")
                };
            }
            else if (arguments.Length != 2)
            {
                throw new ArgumentException("Unknown or repeated arguments. Run dotboxd --help.");
            }
            var result = arguments[0] switch
            {
                "replay" => await ReplayCommand.RunAsync(arguments[1], mode, cancellationToken).ConfigureAwait(false),
                "explain" => await ExplainCommand.RunAsync(arguments[1], cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException("Unknown command. Use explain or replay.")
            };
            await output.WriteLineAsync(json ? JsonSerializer.Serialize(new
            {
                ok = result.Success,
                data = result.Data,
                error = result.Success ? null : new { kind = "execution", message = result.Text },
                meta = new { schemaVersion = 1 }
            }) : EscapeControls(result.Text)).ConfigureAwait(false);
            return result.Success ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or JsonException or FormatException or OverflowException or UnauthorizedAccessException or SandboxValidationException)
        {
            // Exception text may include hostile input; emit a single escaped line in human mode.
            var message = EscapeControls(exception.Message).Replace("\n", " ", StringComparison.Ordinal);
            if (json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    ok = false,
                    data = (object?)null,
                    error = new { kind = "input", message },
                    meta = new { schemaVersion = 1 }
                })).ConfigureAwait(false);
            }
            else
            {
                await error.WriteLineAsync(message).ConfigureAwait(false);
            }
            return 2;
        }
    }
    private static string EscapeControls(string text) => string.Concat(text.Select(character =>
        char.IsControl(character) && character is not '\n' and not '\t'
            ? "\\u" + ((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)
            : character.ToString()));
}

internal sealed record CommandResult(bool Success, object Data, string Text);
