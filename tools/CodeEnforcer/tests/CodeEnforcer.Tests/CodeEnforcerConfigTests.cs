using CodeEnforcer;

namespace CodeEnforcer.Tests;

public sealed class CodeEnforcerConfigTests : IDisposable
{
    private readonly string root;

    public CodeEnforcerConfigTests()
    {
        root = Path.Combine(Path.GetTempPath(), "code-enforcer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void LoadsConfigAndJustificationsFromNearestParentConfigFolder()
    {
        string configDirectory = Path.Combine(root, ".config", "code-enforcer");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "code-enforcer.json"), """
            {
              "version": 1,
              "maxFilesPerDir": 9,
              "maxFilesPerRootDir": 4,
              "maxLinesSoft": 123,
              "maxLinesHard": 456
            }
            """);
        File.WriteAllText(Path.Combine(configDirectory, "justifications.json"), """
            {
              "version": 1,
              "files": [
                {
                  "path": "src/App/Large.cs",
                  "justification": "Legacy file."
                }
              ],
              "folders": [],
              "rootFolders": []
            }
            """);
        string nested = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(nested);

        CodeEnforcerConfig config = CodeEnforcerConfig.Load(nested, configPath: null);

        Assert.Equal(9, config.MaxFilesPerFolder);
        Assert.Equal(4, config.MaxFilesInProjectFolder);
        Assert.Equal(123, config.SoftLineLimit);
        Assert.Equal(456, config.HardLineLimit);
        Assert.NotNull(config.FindFileExclusion("src/App/Large.cs"));
    }

    [Fact]
    public void UsesFirstConfigFoundWhenWalkingParents()
    {
        WriteConfig(Path.Combine(root, ".config", "code-enforcer"), maxFilesPerDir: 9);
        string nestedRoot = Path.Combine(root, "src", ".config", "code-enforcer");
        WriteConfig(nestedRoot, maxFilesPerDir: 7);
        string currentDirectory = Path.Combine(root, "src", "Feature", "Nested");
        Directory.CreateDirectory(currentDirectory);

        CodeEnforcerConfig config = CodeEnforcerConfig.Load(currentDirectory, configPath: null);

        Assert.Equal(7, config.MaxFilesPerFolder);
        Assert.Equal(nestedRoot, config.ConfigDirectory);
    }

    [Fact]
    public void DefaultsRootFolderLimitToFolderLimitWhenOmitted()
    {
        string configDirectory = Path.Combine(root, ".config", "code-enforcer");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "code-enforcer.json"), """
            {
              "version": 1,
              "maxFilesPerDir": 11,
              "maxLinesSoft": 123,
              "maxLinesHard": 456
            }
            """);
        File.WriteAllText(Path.Combine(configDirectory, "justifications.json"), """
            {
              "version": 1,
              "files": [],
              "folders": [],
              "rootFolders": []
            }
            """);

        CodeEnforcerConfig config = CodeEnforcerConfig.Load(root, configPath: null);

        Assert.Equal(11, config.MaxFilesPerFolder);
        Assert.Equal(11, config.MaxFilesInProjectFolder);
    }

    [Fact]
    public void RequiresSiblingJustificationsFile()
    {
        string configDirectory = Path.Combine(root, ".config", "code-enforcer");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "code-enforcer.json"), """
            {
              "version": 1,
              "maxFilesPerDir": 11,
              "maxLinesSoft": 123,
              "maxLinesHard": 456
            }
            """);

        CodeEnforcerException exception = Assert.Throws<CodeEnforcerException>(() =>
            CodeEnforcerConfig.Load(root, configPath: null));

        Assert.Contains("Justifications file does not exist", exception.Message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteConfig(string configDirectory, int maxFilesPerDir)
    {
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "code-enforcer.json"), $$"""
            {
              "version": 1,
              "maxFilesPerDir": {{maxFilesPerDir}},
              "maxLinesSoft": 123,
              "maxLinesHard": 456
            }
            """);
        File.WriteAllText(Path.Combine(configDirectory, "justifications.json"), """
            {
              "version": 1,
              "files": [],
              "folders": [],
              "rootFolders": []
            }
            """);
    }
}
