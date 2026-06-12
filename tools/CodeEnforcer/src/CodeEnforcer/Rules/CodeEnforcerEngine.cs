namespace CodeEnforcer;

internal sealed class CodeEnforcerEngine
{
    public IReadOnlyList<CodeViolation> Check(CodebaseSnapshot snapshot, CodeEnforcerConfig config) =>
        Check(snapshot.Files, snapshot.ProjectFolders, config);

    public IReadOnlyList<CodeViolation> Check(IReadOnlyList<CodeFile> files, CodeEnforcerConfig config)
    {
        HashSet<string> projectFolders = new(StringComparer.Ordinal);
        return Check(files, projectFolders, config);
    }

    public IReadOnlyList<CodeViolation> Check(
        IReadOnlyList<CodeFile> files,
        IReadOnlySet<string> projectFolders,
        CodeEnforcerConfig config)
    {
        List<CodeViolation> violations = [];
        foreach (CodeFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            CheckFile(file, config, violations);
        }

        foreach (IGrouping<string, CodeFile> folder in files.GroupBy(file => file.Folder)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            CheckFolder(folder, config, violations);
            CheckProjectFolder(folder, projectFolders, config, violations);
        }

        return violations;
    }

    private static void CheckFile(
        CodeFile file,
        CodeEnforcerConfig config,
        List<CodeViolation> violations)
    {
        if (file.LineCount <= config.SoftLineLimit)
        {
            return;
        }

        PathExclusion? exclusion = config.FindFileExclusion(file.Path);
        if (exclusion is null)
        {
            string limit = file.LineCount > config.HardLineLimit ? "hard" : "soft";
            violations.Add(new CodeViolation(
                "CE0001",
                file.Path,
                $"has {file.LineCount.ToStringInvariant()} lines, exceeding the {limit} limit. Add an exclusion or split the file."));
            return;
        }

        if (file.LineCount > config.HardLineLimit && string.IsNullOrWhiteSpace(exclusion.Justification))
        {
            violations.Add(new CodeViolation(
                "CE0002",
                file.Path,
                $"has {file.LineCount.ToStringInvariant()} lines, exceeding the hard limit and requiring an exclusion justification."));
        }
    }

    private static void CheckFolder(
        IGrouping<string, CodeFile> folder,
        CodeEnforcerConfig config,
        List<CodeViolation> violations)
    {
        int fileCount = folder.Count();
        if (fileCount <= config.MaxFilesPerFolder || config.IsFolderExcluded(folder.Key))
        {
            return;
        }

        violations.Add(new CodeViolation(
            "CE0003",
            folder.Key,
            $"contains {fileCount.ToStringInvariant()} C# files, exceeding the folder limit of {config.MaxFilesPerFolder.ToStringInvariant()}. Group into subdirectories or move files to a better namespace."));
    }

    private static void CheckProjectFolder(
        IGrouping<string, CodeFile> folder,
        IReadOnlySet<string> projectFolders,
        CodeEnforcerConfig config,
        List<CodeViolation> violations)
    {
        if (!projectFolders.Contains(folder.Key))
        {
            return;
        }

        int fileCount = folder.Count();
        if (fileCount <= config.MaxFilesInProjectFolder || config.IsProjectFolderExcluded(folder.Key))
        {
            return;
        }

        violations.Add(new CodeViolation(
            "CE0004",
            folder.Key,
            $"contains a .csproj and {fileCount.ToStringInvariant()} C# files, exceeding the project-folder limit of {config.MaxFilesInProjectFolder.ToStringInvariant()}. Move implementation files into subdirectories or add a justified project-folder exclusion."));
    }
}
