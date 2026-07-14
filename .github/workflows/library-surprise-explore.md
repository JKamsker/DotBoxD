---
description: |
  Per-lens discovery agent for the surprise-hunt graph. Dispatched by
  library-surprise-dispatcher with one `lens_issue`. Reads the lens charter + its full comment
  trail, dedups against the graph and open PRs, investigates candidates inside that lens
  (read-only), logs the trail as a lens comment, and - per concrete surprise found, up to 5 -
  dispatches the red-test worker. A non-exhausted lens schedules one serialized successor so
  discovery stays continuous without relying on GitHub's lossy cron. It does not edit files or
  open PRs.
  See docs/Task/BugHunting/README.md.

on:
  workflow_dispatch:
    inputs:
      lens_issue:
        description: Lens issue number to mine this run.
        required: true
        type: string
      vein:
        description: The vein:* tag of the lens (informational).
        required: false
        type: string

run-name: "explore #${{ inputs.lens_issue }} ${{ inputs.vein }}"

concurrency:
  group: explore-${{ inputs.lens_issue }}
  cancel-in-progress: false

# Discovery needs enough wall-clock and model budget to map several adjacent seams instead of
# returning immediately after the first candidate. Per-lens concurrency still bounds this to one
# active explore per lens.
timeout-minutes: 60
max-ai-credits: 3000

permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read

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
  # Builds used to confirm a lead can legitimately exceed Codex's 120-second default.
  timeout: 900
  github:
    lockdown: false
    min-integrity: none

safe-outputs:
  mentions: false
  allowed-github-references: []
  add-comment:
    target: "*"
    max: 3
  add-labels:
    target: "*"
    max: 3
  create-issue:
    max: 5
    labels: [sweep:bug]
  dispatch-workflow:
    workflows: [library-surprise-red-test, library-surprise-explore-continuation]
    # Up to five red-test handoffs plus exactly one continuation handoff.
    max: 6
  noop:
    report-as-issue: false
  missing-tool: false
  missing-data: false
  report-incomplete:
    create-issue: false

engine:
  id: codex
  model: gpt-5.6-sol
  args:
    - " -c"
    - model_reasoning_effort="high"
---

# Library Surprise Explore (per-lens)

You are mining **one lens** of the surprise-hunt graph: issue **#${{ inputs.lens_issue }}**
(vein `${{ inputs.vein }}`). Use two local skills:

- `.codex/skills/surprise-hunt-graph/SKILL.md` - the graph protocol (bookkeeping + coordination).
- `.codex/skills/library-surprise-sweep/SKILL.md` - the technique (how to find + specify a surprise).

Do **not** edit repository files and do **not** create a pull request in this run. This agent
specializes in scoped discovery, graph bookkeeping, and handoff only.

## 1. Load the lens

Read lens issue **#${{ inputs.lens_issue }}** with the GitHub tools: its body (the charter and
likely targets) and **all of its comments** (the trail of what prior runs already checked, found,
refuted, or exhausted). Stay inside this lens's scope for the whole run.

## 2. Required duplicate / dedup check (open AND closed)

Before selecting a candidate, search broadly enough to catch semantic duplicates:

- `[surprise-red-test]` PRs - **open and merged/closed** (`gh pr list --state all`) - and PRs
  labeled `bug`, that overlap the API, attribute, diagnostic ID, generator path, runtime behavior,
  or failing shape you are considering
- `sweep:bug` issues (open or closed)
- the lens's own comment trail - including every candidate previously dispatched from this lens and
  every lead previously **refuted** here (refuted leads live in the comment trail, not as issues)

Treat a candidate as already covered only if it has a corresponding `[surprise-red-test]` PR
(open OR merged/closed) or a `sweep:bug` issue - the PR/issue is the authority. If the comment trail
shows a candidate was **dispatched but no matching PR exists** (its red-test run was cancelled or
failed), re-dispatching it is correct - do not treat the bare "dispatched" log line as coverage.
Never re-mine a lead already **refuted** in the trail.

## 3. Investigate a sustained batch (up to five candidates)

Pick one cohesive **target seam** within this lens: a charter sub-area not yet swept, or the
continuation of an open thread from the comments. Map its focused promise surface and build a small
surprise inventory per the technique skill. You may run restore/build to confirm a lead; you must
not edit files.

Do **not** stop merely because you found the first candidate. After recording a concrete candidate,
continue through the seam's next unexamined promise surface while you still have useful research
time. Repeat until you have five independently concrete candidates, the chosen seam is genuinely
dry, or the remaining time is needed to log and dispatch the work safely. Use the longer run to
inspect multiple adjacent types/methods/shapes, not to repeatedly restate one defect.

Each candidate needs its own distinct failing shape. A concrete candidate parked under "promising
next leads" costs another explore run to re-derive, so harvest everything that already meets the bar
while you hold the context. Never pad toward the cap, split one defect into several candidates, or
lower the concreteness bar: a speculative dispatch is worse than none, but stopping after the first
finding while proven independent findings remain is a wasted run.

## 4. Log the trail on the lens (always)

Post one concise progress comment on lens issue **#${{ inputs.lens_issue }}**
(`add-comment`, target = ${{ inputs.lens_issue }}) recording: what you checked this run, promising
leads for next time, dead ends, any lead you **refuted** (so it is not re-mined), and - for **each**
red-test you dispatch below - its exact `candidate_title` and failing shape. This comment is the
durable dedup ledger: a red-test PR may later be merged and closed, so every dispatched candidate
MUST be recorded here or a future run will re-dispatch it. It is also what lets a concurrently
running red-test from another lens detect an in-flight duplicate before its PR exists. Keep it
short and specific.

## 5. Outcome

Exactly one of:

- **Concrete, non-duplicate surprise(s) found** - for each candidate (up to 5), dispatch the
  red-test worker by calling the **dedicated** `library_surprise_red_test` safe-output tool once
  per candidate (NOT the generic `dispatch_workflow` tool, which is a no-op in this runtime) with:
  - `candidate_title`: concise, PR-title-ready, without the `[surprise-red-test] ` prefix.
  - `candidate_payload`: compact JSON with `bug`, `expected_red_test`, `expected_failure`,
    `suggested_test_area`, `suggested_source_area`, `duplicate_check`, and
    `lens_issue`: "${{ inputs.lens_issue }}".

  Multiple candidates must be mutually distinct defects - if two would be fixed by the same
  production change, dispatch only the stronger one.

  If (and only if) the finding needs tracking beyond a single PR - it is not yet reducible to one
  red test, or it is a cluster of related surprises - also `create-issue` (`sweep:bug`) with a repro
  and a `Lens: #${{ inputs.lens_issue }}` line. Otherwise the dispatched red-test PR is the bug
  record; do not create a redundant issue.

- **This lens is swept dry** (no remaining leads across the charter) - say so in the comment and
  `add-labels` `sweep:exhausted` on issue #${{ inputs.lens_issue }}.

- **Nothing actionable this run** (but the lens is not exhausted) - leave the progress comment and
  call `noop` with the reason.

Never weaken a guardrail or dispatch a speculative candidate to appear productive; prefer `noop`.

## 6. Continue non-exhausted lenses

After completing the outcome above, schedule exactly one successor when this lens remains active:
call the dedicated `library_surprise_explore_continuation` safe-output tool with the same
`lens_issue: "${{ inputs.lens_issue }}"` and `vein: "${{ inputs.vein }}"`. Do this after a candidate
batch **and** after a non-exhausting `noop`. The continuation re-checks the live lens labels and
queues the next explore behind this run's per-lens concurrency lock, creating continuous scanning
without overlap.

Do not schedule a successor when you mark the whole lens `sweep:exhausted`. Do not call the generic
`dispatch_workflow` tool. If this run fails before emitting its successor, the scheduled dispatcher
is the recovery path.

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint hosts, or full
endpoint URLs.
