# AGENTS.md

## Repository Expectations

- Keep changes small and reviewable.
- Prefer maintainable, direct code over clever code.
- Add or update tests for behavior changes.
- Add or update benchmarks or allocation tests for hot-path performance changes where practical.
- Do not claim performance improvements without evidence.
- Do not broaden public API without explaining why.
- Run relevant validation before handoff.

## C# Size Guard

- Non-generated C# files should stay under 300 lines where practical.
- `CodeEnforcer` fails tracked C# files over 350 lines unless they are listed in `.config/code-enforcer/justifications.json`.
- Files over 500 lines require both an exclusion and a non-empty justification.
- Folders over 15 tracked C# files must be listed in `.config/code-enforcer/justifications.json`; prefer focused subdirectories and namespaces for new code.
- Folders containing a `.csproj` may contain at most 5 tracked C# files unless listed in `.config/code-enforcer/justifications.json`.
- Split large code through composition and focused helper types, not partial classes used only to hide line count.

## Validation

- Build: `dotnet build DotBoxD.slnx -c Release`.
- Test: `dotnet test DotBoxD.slnx -c Release` (run per-project on CI; see `.github/workflows/ci.yml`).
- Quality gates live in `eng/scripts/` (security-boundary suite, API baselines, file-length, spec
  manifest, rebrand-completeness, docs smoke) and run in CI.
