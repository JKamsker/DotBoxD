---
id: COR-0007
area: correctness
status: open
priority: medium
title: AgentQueue doctor accepts statuses the renderer omits
dedup_key: correctness/agentqueue/status-case/doctor-render-mismatch
created_at: 2026-06-12T21:01:06.1315345+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T21:01:06.1315345+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0007: AgentQueue doctor accepts statuses the renderer omits

## Claim

`agentq doctor` accepts case-variant status values such as `Open` or `CLAIMED`, but the renderer and transition logic treat statuses as exact lowercase tokens. A finding with a case-variant status can be omitted from the generated queue while doctor still considers the status valid.

## Evidence

`tools/AgentQueue/src/AgentQueue/Core/AgentQueueCatalog.cs` defines lowercase canonical statuses, but `IsStatus` uses `Statuses.Contains(value, StringComparer.OrdinalIgnoreCase)`. `QueueDoctor.CheckFinding` relies on `IsStatus`, so frontmatter `status: Open` passes the status validation.

The rest of the queue uses case-sensitive checks. `QueueRenderer.Generate` places findings into sections with comparisons like `finding.Status == "open"`, `finding.Status == "claimed"`, and `finding.Status == "fixed_pending_verification"`; `IsFinalStatus` also checks exact lowercase final status literals. `CanTransition` switches on exact lowercase `current` values. Therefore a finding whose status is `Open` is not rendered in any section, and a queue generated from that same corrupted state can match doctor output while the finding is effectively invisible in the generated view.

Existing AgentQueue workflow tests cover normal lowercase append/claim/fix/verify and self-duplicate validation, but they do not cover doctor rejection or normalization of case-variant status/priority values.

## Risk

The generated queue is the operational view for producers, fixers, and verifiers. If a finding file is edited or merged with a case-variant status, doctor can report the queue healthy while the finding is absent from the queue and cannot transition through normal commands without manual repair.

## Suggested test

Add an AgentQueue doctor test that appends a finding, edits its frontmatter from `status: open` to `status: Open`, renders the queue from that state, and then runs `agentq doctor`. Doctor should fail with a canonical-status diagnostic rather than accepting the omitted finding. Add the same coverage for `priority: High` if priorities are also intended to be canonical lowercase.

## Expected behavior

Doctor should enforce canonical lowercase values for status and priority, or the repository should normalize frontmatter values before rendering and transitions. A non-canonical status must not be allowed to disappear from generated queue sections.

## Suggested fix direction

Make `AgentQueueCatalog.IsStatus` and `IsPriority` use `StringComparer.Ordinal` for stored frontmatter validation, or add explicit canonical-value checks in `QueueDoctor.CheckFinding`. Keep command-line area/priority parsing case-insensitive only if commands normalize the stored value to the canonical catalog token before saving.

## Deduplication key

`correctness/agentqueue/status-case/doctor-render-mismatch`
