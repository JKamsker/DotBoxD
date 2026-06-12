using System.Text.Json;

namespace CodeEnforcer;

internal sealed class CodeEnforcerJustifications
{
    public int Version { get; set; } = 1;

    public List<PathExclusion> Files { get; set; } = [];

    public List<PathExclusion> Folders { get; set; } = [];

    public List<PathExclusion> RootFolders { get; set; } = [];

    public static CodeEnforcerJustifications Load(string configDirectory)
    {
        string path = Path.Combine(configDirectory, "justifications.json");
        if (!File.Exists(path))
        {
            throw new CodeEnforcerException(
                $"Justifications file does not exist: {path}",
                ExitCodes.InputError);
        }

        CodeEnforcerJustifications? justifications = JsonSerializer.Deserialize<CodeEnforcerJustifications>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return justifications ?? throw new CodeEnforcerException(
            "Justifications file is empty.",
            ExitCodes.InputError);
    }

    public void ApplyTo(CodeEnforcerConfig config)
    {
        config.FileExclusions = Files;
        config.FolderExclusions = Folders;
        config.ProjectFolderExclusions = RootFolders;
    }
}
