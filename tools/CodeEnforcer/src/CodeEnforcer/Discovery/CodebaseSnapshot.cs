namespace CodeEnforcer;

internal sealed record CodebaseSnapshot(
    IReadOnlyList<CodeFile> Files,
    IReadOnlySet<string> ProjectFolders);
