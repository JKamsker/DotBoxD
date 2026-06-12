# CodeEnforcer

`CodeEnforcer` is the repository-local C# structure gate.

Run it through the existing CI/hook script:

```powershell
./scripts/check-csharp-file-lines.ps1
```

Or directly:

```powershell
dotnet run --project tools/CodeEnforcer/src/CodeEnforcer
```

By default, CodeEnforcer starts from the current working directory and walks up parent directories until it finds:

```text
.config/code-enforcer/code-enforcer.json
```

The same folder must also contain:

```text
.config/code-enforcer/justifications.json
```

Config fields:

- `maxFilesPerDir`
- `maxFilesPerRootDir` (defaults to `maxFilesPerDir`)
- `maxLinesSoft`
- `maxLinesHard`

Justifications are stored separately in `justifications.json` with `files`, `folders`, and `rootFolders` arrays. Each entry has a `path`; hard line-limit exemptions must also have a non-empty `justification`.

Rules:

- A tracked C# file over 350 lines must be listed in `justifications.json`.
- A tracked C# file over 500 lines must be listed and have a non-empty justification.
- A folder with more than 15 tracked C# files must be listed in `justifications.json`.
- A folder containing a `.csproj` can have at most 5 tracked C# files unless listed in `justifications.json`.

Exclusions are central on purpose so technical debt is visible in review.
