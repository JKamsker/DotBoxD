# Agentic Workflows in DotBoxD

This repository uses `gh aw` to compile agentic workflow source files into
GitHub Actions workflow locks.

We use the DotBoxD fork of `gh-aw`, not the upstream GitHub release:

https://github.com/JKamsker/gh-aw

The pinned fork release is `v0.82.0-jk.1`. This fork carries the secret-backed
OpenAI-compatible endpoint support DotBoxD needs for Codex workflows.

## File Layout

- `.github/workflows/*.md` are the source files humans edit.
- `.github/workflows/*.lock.yml` are generated GitHub Actions workflows. Do not
  hand-edit these except to inspect generated output during review.
- `.github/aw/actions-lock.json` records action pins used by compiled workflows.

The currently compiled agentic workflows are:

- `.github/workflows/gh-aw-smoke-test.md`
- `.github/workflows/library-surprise-sweep.md`

## Local Setup

Install the forked extension:

```powershell
gh extension remove gh-aw
gh extension install JKamsker/gh-aw --pin v0.82.0-jk.1
gh aw version
```

Expected version output:

```text
gh aw version v0.82.0-jk.1
```

## Editing Workflow Sources

Edit the `.md` source workflow, then regenerate the lock files:

```powershell
gh aw compile --approve --force-refresh-action-pins
```

The forked compiler should emit setup actions pinned to this repository:

```yaml
uses: JKamsker/gh-aw/actions/setup@<commit-sha> # v0.82.0-jk.1
```

It should not emit `github/gh-aw-actions/setup` for these workflows.

## Custom Endpoint and Tokens

DotBoxD routes Codex through a secret-backed OpenAI-compatible endpoint. Source
workflows should declare only the secret name:

```yaml
sandbox:
  agent:
    targets:
      openai:
        base-url-secret: CODEX_LB_BASE_URL
```

The compiled workflow reads `${{ secrets.CODEX_LB_BASE_URL }}` on the runner,
patches the AWF OpenAI target at runtime, and excludes `CODEX_LB_BASE_URL` from
the agent sandbox environment. Do not print, inspect, or summarize the secret
value.

Custom API tokens should also be passed by secret-backed environment variables,
for example:

```yaml
engine:
  id: codex
  env:
    OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
```

Use the repository secret name that matches the target workflow. Never commit
token values.

## Validation

After regeneration, run:

```powershell
gh aw compile --no-emit --validate --approve
git diff --check
```

For changes that affect CI behavior, also run the usual repository validation:

```powershell
dotnet format whitespace DotBoxD.slnx --verify-no-changes --no-restore
$env:GITHUB_ACTIONS='true'; dotnet build DotBoxD.slnx -c Release
dotnet test DotBoxD.slnx -c Release --no-build
```

If `gh aw` reports safe-update changes for new actions, secrets, or redirects,
review them explicitly in the PR description.
