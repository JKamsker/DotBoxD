---
id: COR-0001
area: correctness
status: verified
priority: medium
title: AgentQueue allows self-duplicate findings
dedup_key: correctness/agentqueue/duplicate/self-reference
created_at: 2026-06-12T20:36:43.7146628+00:00
created_by: correctness-auditor
created_commit: 
updated_at: 2026-06-12T20:51:55.2311321+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:48:50.2068350+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:50:22.4288255+00:00
fixed_commit: working-tree
verified_by: verifier
verified_at: 2026-06-12T20:51:55.2311321+00:00
verified_commit: working-tree
duplicate_of: 
---

# COR-0001: AgentQueue allows self-duplicate findings

## Claim

`agentq duplicate` allows a finding to be marked as a duplicate of itself, creating a nonsensical terminal state that `agentq doctor` does not reject.

## Evidence

In `tools/AgentQueue/src/AgentQueue/Infrastructure/QueueMutationCommands.cs`, `Finalize` handles `status == "duplicate"` by reading `--of`, calling `repository.FindRequired(duplicateOf)`, and storing `duplicate_of` without checking that `duplicateOf` differs from `finding.Id`. In `tools/AgentQueue/src/AgentQueue/Infrastructure/QueueDoctor.cs`, validation checks IDs, statuses, priorities, area/filename, dedup keys, body, events, and queue freshness, but it does not validate that duplicate findings point at another finding.

Minimal reproduction:

```powershell
.\scripts\agentq.ps1 append --area correctness --priority medium --title "Example" --dedup-key "correctness/example/self-duplicate" --agent auditor --body-file body.md
.\scripts\agentq.ps1 duplicate COR-0001 --of COR-0001 --agent auditor --reason "same finding"
.\scripts\agentq.ps1 doctor
```

The duplicate command can succeed and the doctor has no rule to flag `duplicate_of: COR-0001` on `id: COR-0001`.

## Suggested test

Add an AgentQueue workflow test that appends a finding, runs `duplicate <id> --of <same id>`, and asserts the command fails with a user or invalid-transition exit code. Also add or extend a doctor validation test with a crafted self-duplicate finding and assert doctor reports it.

## Expected behavior

A finding must not be allowed to duplicate itself. Either `duplicate` should reject `--of` equal to the target ID, or `doctor` should fail such a queue state, preferably both.

## Suggested fix direction

In `QueueMutationCommands.Finalize`, when `status == "duplicate"`, compare `duplicateOf` with `finding.Id` using `StringComparison.OrdinalIgnoreCase` after resolving the target and throw `AgentQueueException` if they match. Add a `QueueDoctor` invariant that validates `duplicate_of` is non-empty, exists, and is not the same ID for duplicate findings.

## Deduplication key

`correctness/agentqueue/duplicate/self-reference`
