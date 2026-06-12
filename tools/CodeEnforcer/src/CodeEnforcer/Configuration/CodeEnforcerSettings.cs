using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeEnforcer;

internal sealed class CodeEnforcerSettings : CommandSettings
{
    [CommandOption("--root <PATH>")]
    [Description("Repository root to scan. Defaults to the git root containing the discovered config.")]
    public string? RootDirectory { get; init; }

    [CommandOption("--config <PATH>")]
    [Description("Config file path. Defaults to .config/code-enforcer/code-enforcer.json found from cwd parents.")]
    public string? ConfigPath { get; init; }

    [CommandOption("--soft-line-limit <NUMBER>")]
    [Description("Override maxLinesSoft from config.")]
    public int? SoftLineLimit { get; init; }

    [CommandOption("--hard-line-limit <NUMBER>")]
    [Description("Override maxLinesHard from config.")]
    public int? HardLineLimit { get; init; }

    [CommandOption("--max-files-per-folder <NUMBER>")]
    [Description("Override maxFilesPerDir from config.")]
    public int? MaxFilesPerFolder { get; init; }

    [CommandOption("--max-files-per-root-dir <NUMBER>")]
    [Description("Override maxFilesPerRootDir from config.")]
    public int? MaxFilesPerRootDirectory { get; init; }

    [CommandOption("--max-files-in-project-folder <NUMBER>")]
    [Description("Compatibility alias for --max-files-per-root-dir.")]
    public int? LegacyMaxFilesInProjectFolder { get; init; }

    private int? MaxFilesInProjectFolder => MaxFilesPerRootDirectory ?? LegacyMaxFilesInProjectFolder;

    public void ApplyOverrides(CodeEnforcerConfig config)
    {
        config.SoftLineLimit = SoftLineLimit ?? config.SoftLineLimit;
        config.HardLineLimit = HardLineLimit ?? config.HardLineLimit;
        config.MaxFilesPerFolder = MaxFilesPerFolder ?? config.MaxFilesPerFolder;
        config.MaxFilesInProjectFolder = MaxFilesInProjectFolder ?? config.MaxFilesInProjectFolder;
        config.Validate();
    }

    public override ValidationResult Validate()
    {
        return FirstPositiveIntError(
                (SoftLineLimit, "--soft-line-limit"),
                (HardLineLimit, "--hard-line-limit"),
                (MaxFilesPerFolder, "--max-files-per-folder"),
                (MaxFilesPerRootDirectory, "--max-files-per-root-dir"),
                (LegacyMaxFilesInProjectFolder, "--max-files-in-project-folder"))
            ?? ValidationResult.Success();
    }

    private static ValidationResult? FirstPositiveIntError(params (int? Value, string Option)[] values)
    {
        foreach ((int? value, string option) in values)
        {
            if (value <= 0)
            {
                return ValidationResult.Error($"{option} expects a positive integer.");
            }
        }

        return null;
    }
}
