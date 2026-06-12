using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeEnforcer;

internal sealed class CodeEnforcerConfig
{
    public int Version { get; set; } = 1;

    [JsonPropertyName("maxLinesSoft")]
    public int SoftLineLimit { get; set; } = 350;

    [JsonPropertyName("maxLinesHard")]
    public int HardLineLimit { get; set; } = 500;

    [JsonPropertyName("maxFilesPerDir")]
    public int MaxFilesPerFolder { get; set; } = 15;

    [JsonPropertyName("maxFilesPerRootDir")]
    public int MaxFilesInProjectFolder { get; set; }

    [JsonIgnore]
    public List<PathExclusion> FileExclusions { get; set; } = [];

    [JsonIgnore]
    public List<PathExclusion> FolderExclusions { get; set; } = [];

    [JsonIgnore]
    public List<PathExclusion> ProjectFolderExclusions { get; set; } = [];

    [JsonIgnore]
    public string ConfigDirectory { get; private set; } = string.Empty;

    public static CodeEnforcerConfig Load(string currentDirectory, string? configPath)
    {
        string fullPath = ResolveConfigPath(currentDirectory, configPath);
        if (!File.Exists(fullPath))
        {
            throw new CodeEnforcerException($"Config file does not exist: {fullPath}", ExitCodes.InputError);
        }

        CodeEnforcerConfig? config = JsonSerializer.Deserialize<CodeEnforcerConfig>(
            File.ReadAllText(fullPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config is null)
        {
            throw new CodeEnforcerException("Config file is empty.", ExitCodes.InputError);
        }

        config.ConfigDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        CodeEnforcerJustifications.Load(config.ConfigDirectory).ApplyTo(config);
        config.Validate();
        return config;
    }

    public PathExclusion? FindFileExclusion(string path) =>
        FileExclusions.FirstOrDefault(exclusion => PathPattern.IsMatch(path, exclusion.Path));

    public bool IsFolderExcluded(string path) =>
        FolderExclusions.Any(exclusion => PathPattern.IsMatch(path, exclusion.Path));

    public bool IsProjectFolderExcluded(string path) =>
        ProjectFolderExclusions.Any(exclusion => PathPattern.IsMatch(path, exclusion.Path));

    public void Validate()
    {
        if (MaxFilesInProjectFolder == 0)
        {
            MaxFilesInProjectFolder = MaxFilesPerFolder;
        }

        if (SoftLineLimit <= 0 || HardLineLimit <= 0 ||
            MaxFilesPerFolder <= 0 || MaxFilesInProjectFolder <= 0)
        {
            throw new CodeEnforcerException("Limits must be positive.", ExitCodes.InputError);
        }

        if (SoftLineLimit > HardLineLimit)
        {
            throw new CodeEnforcerException("maxLinesSoft must be <= maxLinesHard.", ExitCodes.InputError);
        }

        IEnumerable<PathExclusion> exclusions = FileExclusions
            .Concat(FolderExclusions)
            .Concat(ProjectFolderExclusions);

        foreach (PathExclusion exclusion in exclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion.Path))
            {
                throw new CodeEnforcerException("Exclusion paths must not be empty.", ExitCodes.InputError);
            }
        }
    }

    private static string ResolveConfigPath(string currentDirectory, string? configPath)
    {
        if (configPath is not null)
        {
            return Path.IsPathRooted(configPath)
                ? configPath
                : Path.GetFullPath(Path.Combine(currentDirectory, configPath));
        }

        return RepositoryPaths.DiscoverConfigPath(currentDirectory);
    }
}
