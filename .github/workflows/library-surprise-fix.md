---
description: |
  Fixes the production issue behind one open [surprise-red-test] PR on the same
  PR branch (FIX mode), or — for an already-sweep:fixed PR re-dispatched because
  its CI is red or CodeRabbit is unaddressed — merges main, makes CI green, and
  addresses/resolves CodeRabbit (GREEN/POLISH mode). Dispatched per-PR by
  library-surprise-fix-dispatcher; re-proves the red regression test locally
  (red then green) inside this run rather than depending on the approval-gated
  pull_request CI.

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: The [surprise-red-test] pull request number to fix.
        required: true
        type: string

run-name: "fix #${{ inputs.pr_number }}"

# Per-PR group: distinct fix dispatches for different PRs run in parallel and never
# cancel each other; a duplicate dispatch for the SAME PR queues instead of double-fixing.
concurrency:
  group: surprise-fix-${{ inputs.pr_number }}
  cancel-in-progress: false

permissions:
  actions: read
  contents: read
  issues: read
  pull-requests: read

checkout:
  fetch-depth: 0
  fetch: ["*"]

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
  push-to-pull-request-branch:
    target: "*"
    required-title-prefix: "[surprise-red-test] "
    required-labels: [bug, ".NET"]
    max: 1
    max-patch-size: 8192
    if-no-changes: "error"
    protected-files: fallback-to-issue
    # Full token override: push the fix commit as the PAT's user (not github-actions[bot]) so
    # the resulting pull_request CI runs execute without manual approval. A PAT push is not
    # subject to recursion-prevention, so CI triggers directly — no extra empty commit needed.
    # Mirrors library-surprise-red-test.md's create-pull-request override; without this the
    # fix commit was bot-pushed and every fixed PR's final CI sat at action_required (#266).
    # Requires the PAT to have Contents:R/W + Pull requests:R/W.
    github-token: ${{ secrets.GH_AW_CI_TRIGGER_TOKEN }}
  add-labels:
    target: "*"
    max: 1
  add-comment:
    # The polish/fix summary comment. Its body carries the machine-readable
    # `<!-- surprise-fix:resolve {...} -->` block a post-step uses to resolve the
    # CodeRabbit threads the agent actually handled, and the
    # `<!-- surprise-fix:polished -->` marker the dispatcher reads to know a polish
    # pass already ran.
    target: "*"
    max: 2
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
  - name: Resolve eligible surprise fix target
    id: surprise-fix-target
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
      EVENT_NAME: ${{ github.event_name }}
      DISPATCH_PR_NUMBER: ${{ inputs.pr_number || '' }}
      SURPRISE_PR_TITLE_PREFIX: "[surprise-red-test] "
      # NOTE: only workflow_dispatch remains; EVENT_NAME kept for the resolver's guard.
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw
      python3 - <<'PY'
      import json
      import os
      import subprocess
      import sys

      event_name = os.environ["EVENT_NAME"]
      repo = os.environ["GITHUB_REPOSITORY"]
      owner, _, repo_name = repo.partition("/")
      prefix = os.environ["SURPRISE_PR_TITLE_PREFIX"]
      dispatch_pr_number = os.environ.get("DISPATCH_PR_NUMBER", "").strip()
      event_path = os.environ.get("GITHUB_EVENT_PATH")

      with open(event_path, encoding="utf-8") as handle:
          event = json.load(handle)

      target = {
          "should_run": False,
          "reason": "No eligible surprise PR target was found.",
          "event_name": event_name,
          "pr_number": None,
          "pr_url": None,
          "head_ref": None,
          "head_sha": None,
          "ci_proof": None,
          "is_fixed": False,
      }

      def run_json(args):
          result = subprocess.run(args, check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
          if result.returncode != 0:
              print(result.stderr, file=sys.stderr)
              raise SystemExit(result.returncode)
          return json.loads(result.stdout or "null")

      def gather_coderabbit(number):
          """Collect CodeRabbit inline threads (with node ids for resolution) and
          top-level review bodies so the agent can judge and address them without
          re-deriving them via the API."""
          query = (
              "query($owner:String!,$name:String!,$pr:Int!){repository(owner:$owner,name:$name){"
              "pullRequest(number:$pr){"
              "reviewThreads(first:60){nodes{id isResolved isOutdated path line "
              "comments(first:1){nodes{author{login} body}}}}"
              "reviews(first:40){nodes{author{login} state submittedAt body}}}}}"
          )
          result = subprocess.run(
              ["gh", "api", "graphql", "-F", f"owner={owner}", "-F", f"name={repo_name}",
               "-F", f"pr={number}", "-f", f"query={query}"],
              check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
          if result.returncode != 0:
              print(result.stderr, file=sys.stderr)
              return {"threads": [], "reviews": [], "gathered": False}
          pr_node = (((json.loads(result.stdout or "{}").get("data") or {})
                      .get("repository") or {}).get("pullRequest") or {})
          threads = []
          for t in (pr_node.get("reviewThreads") or {}).get("nodes") or []:
              cnodes = (t.get("comments") or {}).get("nodes") or []
              c0 = cnodes[0] if cnodes else {}
              author = (c0.get("author") or {}).get("login", "")
              if not author.lower().startswith("coderabbit"):
                  continue
              threads.append({
                  "id": t.get("id"),
                  "isResolved": t.get("isResolved"),
                  "isOutdated": t.get("isOutdated"),
                  "path": t.get("path"),
                  "line": t.get("line"),
                  "author": author,
                  "body": (c0.get("body") or "")[:4000],
              })
          reviews = []
          for r in (pr_node.get("reviews") or {}).get("nodes") or []:
              author = (r.get("author") or {}).get("login", "")
              body = r.get("body") or ""
              if not author.lower().startswith("coderabbit") or not body.strip():
                  continue
              reviews.append({
                  "author": author,
                  "state": r.get("state"),
                  "submittedAt": r.get("submittedAt"),
                  "body": body[:6000],
              })
          return {"threads": threads, "reviews": reviews, "gathered": True}

      coderabbit = {"threads": [], "reviews": [], "gathered": False}
      pr_number = None
      if event_name == "workflow_run":
          workflow_run = event.get("workflow_run") or {}
          if workflow_run.get("event") != "pull_request":
              target["reason"] = "Completed ci run was not for a pull_request event."
          elif workflow_run.get("conclusion") != "failure":
              target["reason"] = f"Completed ci run conclusion was {workflow_run.get('conclusion')!r}, not failure."
          else:
              pull_requests = workflow_run.get("pull_requests") or []
              if len(pull_requests) != 1:
                  target["reason"] = f"Expected one associated pull request, found {len(pull_requests)}."
              else:
                  pr_number = pull_requests[0].get("number")
                  target["ci_proof"] = {
                      "source": "workflow_run",
                      "run_id": workflow_run.get("id"),
                      "run_url": workflow_run.get("html_url"),
                      "head_sha": workflow_run.get("head_sha"),
                      "conclusion": workflow_run.get("conclusion"),
                  }
      elif event_name == "workflow_dispatch":
          if dispatch_pr_number:
              pr_number = dispatch_pr_number
          else:
              target["reason"] = "Manual dispatch did not provide pr_number."
      else:
          target["reason"] = f"Unsupported event: {event_name}"

      if pr_number is not None:
          pr = run_json(
              [
                  "gh",
                  "pr",
                  "view",
                  str(pr_number),
                  "--repo",
                  repo,
                  "--json",
                  "number,title,state,url,headRefName,headRefOid,headRepository,baseRefName,isCrossRepository,labels,commits",
              ]
          )
          label_names = {label.get("name") for label in pr.get("labels") or []}
          head_repository = (pr.get("headRepository") or {}).get("nameWithOwner")
          errors = []
          if pr.get("state") != "OPEN":
              errors.append("PR is not open")
          if not str(pr.get("title") or "").startswith(prefix):
              errors.append(f"PR title does not start with {prefix!r}")
          for required_label in ["bug", ".NET"]:
              if required_label not in label_names:
                  errors.append(f"PR is missing required label {required_label!r}")
          if pr.get("isCrossRepository"):
              errors.append("PR is from a fork or different repository")
          if head_repository and head_repository != repo:
              errors.append(f"PR head repository is {head_repository!r}, expected {repo!r}")
          if pr.get("headRefName") == pr.get("baseRefName"):
              errors.append("PR head branch equals base branch")

          if not target["ci_proof"]:
              runs = run_json(
                  [
                      "gh",
                      "run",
                      "list",
                      "--repo",
                      repo,
                      "--workflow",
                      "ci.yml",
                      "--event",
                      "pull_request",
                      "--limit",
                      "50",
                      "--json",
                      "databaseId,status,conclusion,headSha,url,createdAt,displayTitle",
                  ]
              )
              commit_oids = {
                  commit.get("oid")
                  for commit in pr.get("commits") or []
                  if commit.get("oid")
              }
              commit_oids.add(pr.get("headRefOid"))
              failed_runs = [
                  run
                  for run in runs
                  if run.get("status") == "completed"
                  and run.get("conclusion") == "failure"
                  and run.get("headSha") in commit_oids
              ]
              if failed_runs:
                  run = failed_runs[0]
                  target["ci_proof"] = {
                      "source": "run_list",
                      "run_id": run.get("databaseId"),
                      "run_url": run.get("url"),
                      "head_sha": run.get("headSha"),
                      "conclusion": run.get("conclusion"),
                  }
              else:
                  # A failing pull_request ci run is a nice-to-have, not required:
                  # this run re-proves the red test locally in its own validation
                  # steps (Build + "Verify fix is green" establish red->green here).
                  pass

          if errors:
              target["reason"] = "; ".join(errors)
          else:
              target.update(
                  {
                      "should_run": True,
                      "reason": "Eligible open [surprise-red-test] PR; red is re-proven locally in this run.",
                      "pr_number": pr.get("number"),
                      "pr_url": pr.get("url"),
                      "head_ref": pr.get("headRefName"),
                      "head_sha": pr.get("headRefOid"),
                      "base_ref": pr.get("baseRefName"),
                      "is_fixed": "sweep:fixed" in label_names,
                  }
              )
              coderabbit = gather_coderabbit(pr.get("number"))

      with open("/tmp/gh-aw/coderabbit.json", "w", encoding="utf-8") as handle:
          json.dump(coderabbit, handle, indent=2, sort_keys=True)

      with open("/tmp/gh-aw/surprise-fix-target.json", "w", encoding="utf-8") as handle:
          json.dump(target, handle, indent=2, sort_keys=True)

      with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as env_file:
          env_file.write(f"SURPRISE_FIX_SHOULD_RUN={str(target['should_run']).lower()}\n")
          env_file.write(f"SURPRISE_FIX_PR_NUMBER={target.get('pr_number') or ''}\n")
          env_file.write(f"SURPRISE_FIX_PR_URL={target.get('pr_url') or ''}\n")
          env_file.write(f"SURPRISE_FIX_IS_FIXED={str(target.get('is_fixed', False)).lower()}\n")

      with open("/tmp/gh-aw/surprise-fix-target.env", "w", encoding="utf-8") as env_file:
          env_file.write(f"surprise_should_run={str(target['should_run']).lower()}\n")
          env_file.write(f"surprise_pr_number={target.get('pr_number') or ''}\n")

      print(json.dumps(target, indent=2, sort_keys=True))
      PY

      . /tmp/gh-aw/surprise-fix-target.env

      if [ "${surprise_should_run:-false}" = "true" ]; then
        # The dispatcher has already merged the latest main into this PR branch
        # (server-side, via the PAT) before dispatching, so the checkout below is
        # already refreshed — stale-branch CI gates that main has since cleared do
        # not need handling here.
        gh pr checkout "$surprise_pr_number" --repo "$GITHUB_REPOSITORY"
        original_head="$(git rev-parse HEAD)"
        echo "SURPRISE_FIX_ORIGINAL_HEAD=${original_head}" >> "$GITHUB_ENV"
      fi

post-steps:
  - name: Check whether fix push validation is required
    id: surprise-fix-guard
    shell: bash
    run: |
      set -euo pipefail
      outputs="/tmp/gh-aw/safeoutputs.jsonl"
      should_validate=false
      if [ -s "$outputs" ] && grep -Eq '"type":"push_to_pull_request_branch"' "$outputs"; then
        should_validate=true
      fi
      echo "should_validate=${should_validate}" >> "$GITHUB_OUTPUT"

  - name: Reject ineligible surprise fix push output
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    shell: bash
    run: |
      set -euo pipefail
      python3 - <<'PY'
      import json
      import os
      import sys

      target_path = "/tmp/gh-aw/surprise-fix-target.json"
      outputs_path = "/tmp/gh-aw/safeoutputs.jsonl"

      with open(target_path, encoding="utf-8") as handle:
          target = json.load(handle)
      if not target.get("should_run"):
          print(f"::error::Refusing push output for ineligible target: {target.get('reason')}", file=sys.stderr)
          sys.exit(1)
      expected_number = int(target["pr_number"])

      push_items = []
      with open(outputs_path, encoding="utf-8") as handle:
          for line_number, line in enumerate(handle, start=1):
              text = line.strip()
              if not text:
                  continue
              item = json.loads(text)
              if item.get("type") == "push_to_pull_request_branch":
                  push_items.append((line_number, item))

      if len(push_items) != 1:
          print(f"::error::Expected exactly one push_to_pull_request_branch output, found {len(push_items)}.", file=sys.stderr)
          sys.exit(1)

      _, item = push_items[0]
      raw_number = (
          item.get("pull_request_number")
          or item.get("pr_number")
          or item.get("pull_number")
          or item.get("pr")
      )
      if int(raw_number) != expected_number:
          print(
              f"::error::Push target PR #{raw_number} does not match resolved target #{expected_number}.",
              file=sys.stderr,
          )
          sys.exit(1)

      print(f"Validated push output target for surprise PR #{expected_number}.")
      PY

  - name: Setup .NET for fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: |
        8.0.x
        9.0.x
        10.0.x

  - name: Materialize safe-output patch for fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    shell: bash
    run: |
      set -euo pipefail
      original_sha="${SURPRISE_FIX_ORIGINAL_HEAD:-}"
      current_sha="$(git rev-parse HEAD)"
      has_workspace_changes=false
      git diff --quiet || has_workspace_changes=true
      git diff --cached --quiet || has_workspace_changes=true

      if [ "$has_workspace_changes" = false ] && [ -n "$original_sha" ] && [ "$current_sha" = "$original_sha" ]; then
        shopt -s nullglob
        patches=(/tmp/gh-aw/aw-*.patch)
        if [ "${#patches[@]}" -eq 0 ]; then
          echo "::error::Safe output requested a PR branch push, but no workspace changes or patch artifact were found."
          exit 1
        fi
        if [ "${#patches[@]}" -gt 1 ]; then
          printf '::error::Expected one safe-output patch, found %s: %s\n' "${#patches[@]}" "${patches[*]}"
          exit 1
        fi
        git apply --check "${patches[0]}"
        git apply "${patches[0]}"
      fi

      git status --short

  - name: Restore before fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    run: dotnet restore DotBoxD.slnx

  - name: Build before fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    run: GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore

  - name: Resolve agent-handled CodeRabbit review threads
    shell: bash
    env:
      # Resolve as the PAT (repo write) rather than the read-only agent job token.
      GH_TOKEN: ${{ secrets.GH_AW_CI_TRIGGER_TOKEN }}
    run: |
      set -euo pipefail
      outputs="/tmp/gh-aw/safeoutputs.jsonl"
      if [ ! -s "$outputs" ]; then
        echo "No safe outputs; nothing to resolve."
        exit 0
      fi
      python3 - <<'PY'
      import json, re, subprocess, sys

      outputs_path = "/tmp/gh-aw/safeoutputs.jsonl"
      # The agent embeds the threads it actually handled (fixed, or consciously
      # dismissed WITH a stated reason) in its summary add_comment body, as:
      #   <!-- surprise-fix:resolve {"resolve":["<threadNodeId>", ...]} -->
      # Only those are resolved here; threads it left genuinely open are untouched.
      marker = re.compile(r"<!--\s*surprise-fix:resolve\s*(\{.*?\})\s*-->", re.DOTALL)

      bodies = []
      with open(outputs_path, encoding="utf-8") as handle:
          for line in handle:
              line = line.strip()
              if not line:
                  continue
              try:
                  item = json.loads(line)
              except json.JSONDecodeError:
                  continue
              if item.get("type") != "add_comment":
                  continue
              for value in item.values():
                  if isinstance(value, str):
                      bodies.append(value)

      thread_ids = []
      for body in bodies:
          found = marker.search(body)
          if not found:
              continue
          try:
              payload = json.loads(found.group(1))
          except json.JSONDecodeError as exc:
              print(f"::warning::Unparseable surprise-fix:resolve marker: {exc}")
              continue
          for tid in payload.get("resolve") or []:
              if isinstance(tid, str) and tid and tid not in thread_ids:
                  thread_ids.append(tid)

      if not thread_ids:
          print("No CodeRabbit threads flagged for resolution.")
          sys.exit(0)

      mutation = ("mutation($id:ID!){resolveReviewThread(input:{threadId:$id})"
                  "{thread{id isResolved}}}")
      resolved = 0
      for tid in thread_ids:
          result = subprocess.run(
              ["gh", "api", "graphql", "-F", f"id={tid}", "-f", f"query={mutation}"],
              check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
          if result.returncode != 0:
              print(f"::warning::Failed to resolve thread {tid}: {result.stderr.strip()}")
          else:
              resolved += 1
              print(f"Resolved review thread {tid}")
      print(f"Resolved {resolved}/{len(thread_ids)} agent-handled CodeRabbit thread(s).")
      PY

  # NOTE: we intentionally do NOT run `dotnet test DotBoxD.slnx` here. The whole-solution
  # test run OOM-kills the DotBoxD.Kernels.Tests host (exit 137) under this job's memory
  # ceiling, and a memory-safe per-project sequential run does not fit the 45-min timeout on
  # top of the agent. The authoritative full-suite gate is the PR's own CI, which now runs
  # automatically and ungated on the fix push (per-project matrix, real runners — see
  # library-surprise-red-test.md / the GH_AW_CI_TRIGGER_TOKEN override on the push below).
  # The restore+build above stays as a fast cross-project compile gate.
---

# Library Surprise Fix

This workflow repairs an existing `[surprise-red-test]` pull request whose red
regression test reproduces a real bug. It is dispatched per-PR (by number) from
`library-surprise-fix-dispatcher`. Do not discover a new surprise in this
workflow.

This worker runs in one of two modes on the same PR branch:

- **FIX** — the bug is not fixed yet (`is_fixed` is `false` in the target and the
  red test still FAILS). Implement the production fix, prove red->green in this
  run, push, label `sweep:fixed`.
- **GREEN / POLISH** — the PR is already `sweep:fixed` (`is_fixed` is `true`), but
  it was re-dispatched because its real PR CI is red and/or it still has
  unresolved or unaddressed CodeRabbit feedback. The bug is already fixed; your
  job is to make the PR's CI fully green and to address + resolve CodeRabbit.

The red->green proof lives in this run: in FIX mode you confirm the red test
FAILS on the checked-out branch, implement the fix, then confirm it PASSES by
running the ENTIRE test project that owns the red test (this catches sibling-test
regressions), and confirm the whole solution BUILDS. Do not run the whole
solution's tests here — a full `dotnet test DotBoxD.slnx` OOM-kills the
DotBoxD.Kernels.Tests host in this sandbox (exit 137); run only the owning
project (Kernels.Tests excepted — for it run the focused `--filter`ed tests
only). The authoritative full-suite gate is the PR's own CI, which now runs
automatically and ungated on your push (per-project, on real runners).

## Target Resolution

Read `/tmp/gh-aw/surprise-fix-target.json` first.

If `SURPRISE_FIX_SHOULD_RUN` is not `true`, leave the workspace unchanged and
call `noop` with the recorded reason. This workflow is intentionally selective:
it may only act on an open, same-repository PR whose title starts with
`[surprise-red-test] `, and has the `bug` and `.NET` labels. A `sweep:fixed`
label does NOT disqualify a PR — that is the GREEN/POLISH entry point.

Before dispatching you, the dispatcher **merged the latest `main`** into this PR
branch (server-side, as the PAT), so your checkout is already refreshed. That is
how stale-branch CI gates main has since cleared (e.g. the repo-wide CE0006
file-length budget) get resolved without you touching unrelated files. If main
and the branch had a merge conflict the merge was skipped — resolve what you can
or note it for a human. Confirm the checked-out branch still contains the red
tests from the PR.

## Determine your mode

Read `is_fixed` in the target and run the focused regression test once:

- `is_fixed` false **and** red test FAILS → **FIX** mode.
- `is_fixed` false **and** red test already PASSES → someone already fixed it:
  leave the workspace unchanged and `noop`.
- `is_fixed` true → **GREEN/POLISH** mode. (If the red test unexpectedly FAILS
  again, the fix regressed — re-implement it as in FIX mode, then continue with
  the GREEN steps.)

## FIX mode

1. Read the failing tests and nearby production code. Do not remove, skip,
   weaken, or loosen the red regression tests.
2. Implement the smallest maintainable production fix. Keep the public design
   rule intact: public abstractions and generators must remain opt-in sugar over
   public primitives, never lock-in.
3. Confirm the fix, then run `dotnet restore DotBoxD.slnx` and
   `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore`.
   To confirm the fix, run the ENTIRE test project that contains the red
   regression test (not just the single focused test) and require it fully green:
   `GITHUB_ACTIONS=true dotnet test <the-red-test's-project> -c Release --no-build`.
   Running the whole owning project catches regressions in sibling tests that a
   single `--filter` misses (e.g. an ArgumentException with the wrong ParamName).
   Do NOT run `GITHUB_ACTIONS=true dotnet test DotBoxD.slnx` (the whole solution)
   — it OOM-kills the DotBoxD.Kernels.Tests host in this sandbox (exit 137). The
   ONE exception is DotBoxD.Kernels.Tests itself: its full run also OOMs here, so
   if the red test lives in DotBoxD.Kernels.Tests, run only the focused
   `--filter`ed test(s) and rely on the PR CI for the rest. Every other test
   project runs fine in this sandbox. The PR's own per-project CI runs
   automatically after your push and is the authoritative full-suite gate.
4. Do the **CodeRabbit pass** (below).
5. Commit the fix on the checked-out PR branch. The commit message must include
   a short summary followed by a body explaining what changed and why.
6. Call `push_to_pull_request_branch` for `SURPRISE_FIX_PR_NUMBER`, then
   `add_labels` to add `sweep:fixed`, then post the **summary comment** (below).

## GREEN / POLISH mode

The bug is already fixed and main is already merged. Make the PR's real CI green
and clear CodeRabbit.

1. Find what is red: `gh pr checks <pr>` and read the failing job's log (the
   "Security & quality gates" job runs the gate scripts in
   `.github/workflows/ci.yml`). Common causes and fixes:
   - **CE0006 file-length budget / CE0002 soft-limit** — usually cleared already
     by the main merge. If any file the CI still flags is over the limit, split
     it into cohesive partial(s) or a themed helper.
   - **`dotnet format` whitespace** — run
     `dotnet format whitespace DotBoxD.slnx --no-restore` and commit the result.
   - **Public API baseline** — a public surface changed; refresh the affected
     package's `docs/api-baselines/*.txt` (commit ONLY the intended package's
     file; `-Update` churns EOL/collation across all files on Windows).
   - **CE0004 folder file-count** — move a test file into a themed subfolder.
   Do whatever it takes to make CI green, even when the root cause is repo-wide
   rather than this PR's own diff — but never weaken the red regression test and
   never touch protected files.
2. Do the **CodeRabbit pass** (below).
3. Re-run `dotnet restore` + `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c
   Release --no-restore`, and the owning test project (same OOM caveats as FIX
   step 3), to confirm nothing broke.
4. If you changed any file, commit it and call `push_to_pull_request_branch` for
   `SURPRISE_FIX_PR_NUMBER`. If nothing needed changing (CI was already green and
   no CodeRabbit work), do NOT push. Either way, post the **summary comment**
   (below) so the dispatcher records that a polish pass ran. Keep the existing
   `sweep:fixed` label (do not remove it).

## CodeRabbit pass

`/tmp/gh-aw/coderabbit.json` contains CodeRabbit's inline `threads` (each with a
GraphQL node `id`, `path`, `line`, `isResolved`, `isOutdated`, and `body`) and
its top-level `reviews` (summary bodies, which hold "🧹 Nitpick" and other items
that are NOT inline threads). If CodeRabbit is still pending, wait briefly and
re-read via `gh`.

For every **unresolved inline thread** and every actionable item in the
**top-level review** bodies:

1. Judge whether the finding is valid and in-scope for this PR.
2. If valid, fix it in the same branch (respect the public design rule and the
   protected-files list).
3. Record a per-item disposition in your summary comment: `fixed`, or
   `dismissed` with a one-line reason (e.g. "intentional; matches sibling API").

Only threads you **fixed** or **consciously dismissed with a stated reason** may
be resolved. Never resolve a thread you are leaving genuinely open.

## Summary comment (always, both modes)

Post exactly one `add_comment` on the PR. Its body MUST:

- start with the marker line `<!-- surprise-fix:polished -->` on its own line,
- summarize the mode, what you changed (CI fixes + fix), and each CodeRabbit
  disposition,
- end with a machine-readable block listing ONLY the inline-thread node ids you
  handled, exactly in this form (a post-step resolves these threads):

  ```
  <!-- surprise-fix:resolve {"resolve":["<threadNodeId>","<threadNodeId>"]} -->
  ```

  Use the `id` values from `coderabbit.json`. If you handled no threads, emit
  `<!-- surprise-fix:resolve {"resolve":[]} -->`.

If the PR is no longer eligible, leave the workspace unchanged and call `noop`.

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
