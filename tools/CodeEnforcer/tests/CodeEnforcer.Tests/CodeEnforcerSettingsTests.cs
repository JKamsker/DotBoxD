using CodeEnforcer;

namespace CodeEnforcer.Tests;

public sealed class CodeEnforcerSettingsTests
{
    [Fact]
    public void RejectsOverrideWhenSoftLimitExceedsHardLimit()
    {
        CodeEnforcerSettings settings = new()
        {
            SoftLineLimit = 501,
            HardLineLimit = 500
        };

        CodeEnforcerException exception = Assert.Throws<CodeEnforcerException>(() =>
            settings.ApplyOverrides(new CodeEnforcerConfig()));

        Assert.Contains("maxLinesSoft", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppliesProjectFolderLimitOverride(bool usePrimaryOption)
    {
        CodeEnforcerSettings settings = usePrimaryOption
            ? new CodeEnforcerSettings { MaxFilesPerRootDirectory = 7 }
            : new CodeEnforcerSettings { LegacyMaxFilesInProjectFolder = 7 };
        CodeEnforcerConfig config = new();

        settings.ApplyOverrides(config);

        Assert.Equal(7, config.MaxFilesInProjectFolder);
    }

    [Fact]
    public void RejectsNonPositiveLimitOverride()
    {
        CodeEnforcerSettings settings = new() { MaxFilesPerFolder = 0 };

        Spectre.Console.ValidationResult result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--max-files-per-folder", result.Message);
    }
}
