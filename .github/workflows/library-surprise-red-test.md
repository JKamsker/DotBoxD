---
description: |
  Takes one discovered library-surprise candidate, adds focused red regression
  tests, verifies the tests fail locally, and opens a [surprise-red-test] PR.

on:
  workflow_dispatch:
    inputs:
      candidate_title:
        description: Candidate title without the [surprise-red-test] prefix.
        required: true
        type: string
      candidate_payload:
        description: Compact JSON handoff from the discovery workflow.
        required: true
        type: string

# Per-run group: distinct red-test dispatches must NOT cancel each other. The gh-aw default
# (a shared `gh-aw-<workflow>` group with cancel-in-progress) dropped findings under the
# dispatcher fan-out, since near-simultaneous dispatches killed each other's queued runs.
concurrency:
  group: surprise-red-test-${{ github.run_id }}
  cancel-in-progress: false

permissions:
  contents: read
  issues: read
  pull-requests: read

checkout:
  fetch-depth: 0

network:
  allowed:
    - defaults
    - github
    - dotnet
    - threat-detection

sandbox:
  agent:
    targets:
      openai:
        base-url-secret: CODEX_LB_BASE_URL

tools:
  github:
    lockdown: false
    min-integrity: none

safe-outputs:
  mentions: false
  allowed-github-references: []
  create-pull-request:
    title-prefix: "[surprise-red-test] "
    labels: [bug, ".NET"]
    draft: false
    max: 1
    if-no-changes: "ignore"
    protected-files: fallback-to-issue
    # Full token override: open the PR as the PAT's user (not github-actions[bot]) so
    # its CI runs execute without manual approval. A PAT-created PR is not subject to
    # recursion-prevention, so CI triggers directly — no extra empty commit needed.
    # Requires the PAT to have Contents:R/W + Pull requests:R/W (verified via _probe-ci-token).
    github-token: ${{ secrets.GH_AW_CI_TRIGGER_TOKEN }}
  dispatch-workflow:
    workflows: [library-surprise-fix-dispatcher]
    max: 1
  noop:
    report-as-issue: false
  missing-tool: false
  missing-data: false
  report-incomplete:
    create-issue: false

engine:
  id: codex
  model: gpt-5.5
  args:
    - " -c"
    - model_reasoning_effort="high"

pre-agent-steps:
  - name: Write surprise candidate handoff
    shell: bash
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw
      python3 - <<'PY'
      import json
      import os

      with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as handle:
          event = json.load(handle)
      inputs = event.get("inputs") or {}
      candidate = {
          "candidate_title": str(inputs.get("candidate_title") or "").strip(),
          "candidate_payload": str(inputs.get("candidate_payload") or "").strip(),
      }
      with open("/tmp/gh-aw/surprise-candidate.json", "w", encoding="utf-8") as handle:
          json.dump(candidate, handle, indent=2, sort_keys=True)
      print(
          json.dumps(
              {
                  "candidate_title": candidate["candidate_title"],
                  "candidate_payload_length": len(candidate["candidate_payload"]),
              },
              indent=2,
              sort_keys=True,
          )
      )
      PY

post-steps:
  - name: Check whether red-test PR validation is required
    id: surprise-red-test-guard
    shell: bash
    run: |
      set -euo pipefail
      outputs="/tmp/gh-aw/safeoutputs.jsonl"
      should_validate=false
      if [ -s "$outputs" ] && grep -Eq '"type":"create_pull_request"' "$outputs"; then
        should_validate=true
      fi
      echo "should_validate=${should_validate}" >> "$GITHUB_OUTPUT"

  - name: Reject duplicate or under-specified surprise PR output
    if: steps.surprise-red-test-guard.outputs.should_validate == 'true'
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
      SURPRISE_PR_TITLE_PREFIX: "[surprise-red-test] "
    run: |
      set -euo pipefail
      python3 - <<'PY'
      import json
      import os
      import subprocess
      import sys

      outputs_path = "/tmp/gh-aw/safeoutputs.jsonl"
      agent_output_path = "/tmp/gh-aw/agent_output.json"
      prefix = os.environ["SURPRISE_PR_TITLE_PREFIX"]
      repo = os.environ["GITHUB_REPOSITORY"]

      def replace_with_noop(reason: str) -> None:
          noop = {"type": "noop", "message": reason}
          with open(outputs_path, "w", encoding="utf-8") as handle:
              handle.write(json.dumps(noop, separators=(",", ":")) + "\n")
          with open(agent_output_path, "w", encoding="utf-8") as handle:
              json.dump({"items": [noop], "errors": []}, handle, separators=(",", ":"))
          print(f"::warning::{reason}", file=sys.stderr)

      create_pr_items = []
      with open(outputs_path, encoding="utf-8") as handle:
          for line_number, line in enumerate(handle, start=1):
              text = line.strip()
              if not text:
                  continue
              try:
                  item = json.loads(text)
              except json.JSONDecodeError as exc:
                  replace_with_noop(f"Skipping malformed red-test safe-output JSON on line {line_number}: {exc}")
                  sys.exit(0)
              if item.get("type") == "create_pull_request":
                  create_pr_items.append(item)

      if not create_pr_items:
          print("No create_pull_request safe output found.")
          sys.exit(0)

      if len(create_pr_items) > 1:
          replace_with_noop("Skipping red-test safe output because more than one create_pull_request item was emitted.")
          sys.exit(0)

      result = subprocess.run(
          [
              "gh",
              "pr",
              "list",
              "--repo",
              repo,
              "--state",
              "open",
              "--limit",
              "100",
              "--json",
              "number,title,url",
          ],
          check=False,
          text=True,
          stdout=subprocess.PIPE,
          stderr=subprocess.PIPE,
      )
      if result.returncode != 0:
          print("::warning::Unable to list open PRs for duplicate validation.", file=sys.stderr)
          print(result.stderr, file=sys.stderr)
          sys.exit(0)

      open_prs = json.loads(result.stdout or "[]")
      open_titles = {pr.get("title", ""): pr for pr in open_prs}

      for item in create_pr_items:
          title = str(item.get("title") or "").strip()
          if not title:
              replace_with_noop("Skipping red-test PR output because create_pull_request omitted a non-empty title.")
              sys.exit(0)

          final_title = title if title.startswith(prefix) else prefix + title
          for candidate_title in {title, final_title}:
              duplicate = open_titles.get(candidate_title)
              if duplicate:
                  replace_with_noop(
                      "Skipping duplicate red-test PR output because an open PR already has the same title: "
                      f"#{duplicate.get('number')} {duplicate.get('url')}"
                  )
                  sys.exit(0)

          body = str(item.get("body") or item.get("description") or "")
          required_sections = ["Duplicate check", "Red test", "Expected failure", "Validation"]
          missing = [section for section in required_sections if section.lower() not in body.lower()]
          if missing:
              replace_with_noop(
                  "Skipping red-test PR output because the body is missing required section(s): " +
                  ", ".join(missing)
              )
              sys.exit(0)

          body_lower = body.lower()
          missing_evidence = []
          if "failed as expected" not in body_lower:
              missing_evidence.append("Failed as expected")
          if "dotnet test" not in body_lower:
              missing_evidence.append("dotnet test")
          if missing_evidence:
              replace_with_noop(
                  "Skipping red-test PR output because the Validation section does not record focused red-test evidence: " +
                  ", ".join(missing_evidence)
              )
              sys.exit(0)

      print(f"Validated {len(create_pr_items)} create_pull_request safe output(s) against {len(open_prs)} open PR(s).")
      PY
---

# Library Surprise Red-Test Worker

Read `/tmp/gh-aw/surprise-candidate.json` first. This workflow specializes in
turning one discovery handoff into focused failing regression tests and a PR.

Use the local skill at `.codex/skills/library-surprise-sweep/SKILL.md` for the
test-writing part of this run, but do not run a new broad discovery sweep unless
the handoff is incomplete. If the handoff is missing, duplicated, obsolete, or
does not describe an actionable bug, leave the workspace unchanged and call
`noop` with the reason.

## Scope

Create at most one cohesive PR. The PR must contain red regression tests only.
Do not implement the production fix, do not relax assertions to make tests pass,
and do not change public API unless a test fixture cannot compile without a
minimal test-only scaffold.

## Required Duplicate Check

Before editing files, verify the discovery handoff against current open pull
requests. Search broadly enough to catch semantic duplicates, not just exact
titles:

- open PRs with titles beginning `[surprise-red-test]`
- open PRs labeled `bug` or touching nearby test/source areas
- open PRs whose title/body mentions the same API, attribute, diagnostic ID,
  source-generator path, runtime behavior, or failing shape from the handoff

Also check for an **in-flight duplicate that has no PR yet**: explores run in parallel across
lenses, and each records every candidate it dispatches as a comment on its `sweep:lens` issue
*before* the corresponding red-test finishes. Scan the comments posted in the last ~2 hours on the
open `sweep:lens` issues (`gh issue list --label "sweep:lens" --state open`, then recent comments).
Ignore the entry for **this run's own candidate** (same `candidate_title` / same `lens_issue` as
the handoff). If another lens's recent entry describes the same underlying defect and its comment
is **older** than this run's dispatch, treat it as a duplicate-in-flight: call `noop` naming that
lens issue and candidate. If this run's entry is the older one — or the overlap is genuinely
ambiguous — proceed; a later integration pass dedups residual twins.

If any open PR already covers the same bug or substantially overlapping failing
shape, do not create a second PR. Leave the workspace unchanged and call `noop`
with the duplicate PR number and the candidate bug you skipped.

When you do create a PR, its body must include a `Duplicate check` section that
lists the open PR search terms you used and the relevant PR numbers you ruled
out. The workflow also rejects exact open-title duplicates before safe outputs
are processed.

## Red-Test Process

1. Read `.codex/skills/library-surprise-sweep/SKILL.md`.
2. Read `/tmp/gh-aw/surprise-candidate.json`.
3. Inspect the suggested public API, source, and test areas from the handoff.
4. Add focused tests in the repository's existing style. Assert the
   user-visible behavior: diagnostic presence/absence, generated source shape,
   runtime round trip, validation error, or exact failure mode.
   **Place each new test file in a themed subfolder — never in a folder that
   already holds 15 `.cs` files.** CodeEnforcer fails the PR with `CE0004` when a
   folder exceeds 15 C# files. Broad generated-test roots such as
   `tests/DotBoxD.Kernels.Tests/PluginAnalyzer/Generated/PluginServer/` are split
   into per-theme subfolders (`Surprise/`, `MemberShape/`, `Lifecycle/`,
   `LiveSettings/`, `RemoteLocal/`, …); reuse the matching subfolder or create a
   new one. Test namespaces are folder-independent (`...Generated`), so this
   never changes a class's namespace. If your natural target folder is at or near
   the cap, create a new themed subfolder instead of adding to it.
5. Run restore/build and the focused test command. The branch must build, and
   the new test must fail because the bug is real.
6. Commit only the red tests and any necessary test data.
7. Use the `create_pull_request` safe-output tool.

## Validation Requirements

Before requesting `create_pull_request`:

- `dotnet restore DotBoxD.slnx` must succeed.
- `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore` must succeed.
- A focused `dotnet test` command for the new regression test must fail for the
  expected reason.
- If the focused test unexpectedly passes, remove the changes and call `noop`.

The workflow's post-step validation checks the safe-output shape, exact open PR
title duplicates, and the PR body's recorded focused red-test evidence. It does
not use a whole-solution post-step `dotnet test` run as the red proof:
safe-output processing is downstream of the agent job, and broad test runs can
hide focused race repros. The in-run focused failing command is the authority.

The companion fixer is driven by the `Library Surprise Fix Dispatcher`, which scans open unfixed
`[surprise-red-test]` PRs (it does NOT depend on PR CI — this run's in-run red proof is the
evidence). **Whenever you call `create_pull_request`, also call the dedicated
`library_surprise_fix_dispatcher` safe-output tool once (no inputs)** so the fixer picks the new
PR up immediately instead of waiting for the dispatcher's cron. Do not call it when you `noop`.
The fix worker implements the production fix on the same PR branch.

## Pull Request Shape

Use a title that omits the configured prefix; safe outputs adds
`[surprise-red-test] `. Prefer the `candidate_title` from the handoff unless it
is stale or inaccurate.

The PR body must include these sections:

- `Bug`: the public promise and the surprising behavior.
- `Red test`: the test files and scenario added.
- `Expected failure`: the failing assertion, diagnostic mismatch, or runtime
  failure the test currently exposes.
- `Duplicate check`: search terms used and open PRs inspected.
- `Validation`: restore/build commands that passed and the focused test command
  that failed as expected.

## Protected files — never touch them

Never include top-level protected files in your patch: `README.md`, `CONTRIBUTING.md`,
`CHANGELOG.md`, `AGENTS.md`, `CLAUDE.md`, `DESIGN.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`,
`Directory.Packages.props`, `NuGet.Config`, `global.json`, lockfiles, or anything under dot-folders.
The push layer hard-blocks the ENTIRE patch when it contains any of them (your work is discarded
into a "[gh-aw] Protected Files" issue instead of landing). Documented samples and doc pages live
under `docs/**`, which is allowed — put documentation updates there. If a protected file genuinely
must change for correctness, say so in a comment/PR body and leave the file itself untouched for a
human.

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint
hosts, or full endpoint URLs.
