---
name: merge-sweep-fixed-prs
description: Merge GitHub pull requests matching the sweep fixed queue, especially `is:pr is:open label:sweep:fixed`, into the current integration branch one at a time. Use when asked to create an aggregate PR that references every source PR with checkboxes, commit/push/close each source PR after merge, keep the checklist updated, make CI green, and address or rebut all CodeRabbit review findings with conversations resolved.
---

# Merge Sweep Fixed PRs

## Workflow

1. Establish the integration target.
   Confirm the current branch, upstream, base branch, and clean worktree. If the current branch is not suitable for a pushed PR, create a `codex/...` branch before merging. Do not lose user changes.

2. Build the source PR ledger.
   Query the queue with the user's search, usually:
   ```bash
   gh pr list --search "is:pr is:open label:sweep:fixed" --json number,title,url,headRefName,baseRefName --limit 100
   ```
   Keep a durable local list of source PR numbers and titles. Exclude the aggregate PR itself if it later matches the search. Re-run the search before finishing and add any newly appeared matching PRs.

3. Merge source PRs one at a time.
   For each source PR:
   - Fetch the head into a temporary local branch.
   - Merge with `--no-ff` and a message that names the source PR and explains why it is being integrated.
   - Resolve conflicts by preserving all already-integrated behavior plus the source PR's intended fix.
   - Run focused tests for the touched behavior before committing when practical.
   - Commit the merge, push the integration branch, then close the source PR with a comment such as `Integrated into #<aggregate-pr>.`

4. Create the aggregate PR after the first pushed merge.
   Open one PR from the integration branch to the intended base. Before drafting the body, read `references/pr-131-description.md` and `references/pr-176-description.md` for concrete examples. Always compose the PR body in a temporary Markdown file and pass it with `--body-file`; do not inline multiline PR descriptions through shell arguments. The body must follow the aggregate PR description rules below. Keep the summary accurate as later PRs are integrated. After every later source PR is pushed and closed, edit the body through a temporary Markdown file to check that PR off. If new matching PRs appear, append them unchecked, merge them, and check them off as completed.

5. Keep the aggregate PR current.
   If the base branch moves or GitHub reports conflicts, merge or rebase the latest base into the integration branch, resolve conflicts, run validation, commit, and push. Prefer a normal merge commit for an aggregate branch unless the user asked for a rebase.

6. Make CI green.
   Run the repo's documented local validation before relying on GitHub. For DotBoxD this usually includes:
   ```bash
   dotnet restore DotBoxD.slnx
   dotnet format DotBoxD.slnx
   dotnet format whitespace DotBoxD.slnx --verify-no-changes --no-restore
   GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore
   dotnet test DotBoxD.slnx -c Release --no-build
   ```
   Also run repository quality gates and coverage gates when CI has them or when production behavior changed. Push any fixes and watch the aggregate PR checks until they pass:
   ```bash
   gh pr checks <aggregate-pr> --watch --interval 30
   ```

7. Address CodeRabbit reviews completely.
   Inspect all CodeRabbit comments and review threads on the aggregate PR after every push that triggers review.
   - Fix valid findings with code/tests, commit, and push.
   - If a finding is wrong or intentionally not changed, reply with the concrete reason.
   - Do not resolve a thread silently. Leave either a fix reference or a rationale.
   - Resolve every addressed review conversation, either in the GitHub UI or with the GraphQL `resolveReviewThread` mutation.
   - Repeat until there are no unresolved actionable CodeRabbit conversations and the CodeRabbit check is passing or complete.

8. Final sanity check.
   Before handoff, verify all of these are true:
   - `gh pr list --search "is:pr is:open label:sweep:fixed"` returns no source PRs left to merge.
   - The aggregate PR body has no unchecked source PR checklist items.
   - The aggregate PR is open, non-draft unless requested otherwise, mergeable, and all required checks are green.
   - The local worktree is clean and pushed.

## Aggregate PR Description

The ideal aggregate PR description is a human summary first, then the source PR ledger. It combines the useful parts of the reference examples:
- `references/pr-131-description.md` shows the desired summary depth: a concise lead-in plus grouped change areas and concrete validation notes.
- `references/pr-176-description.md` shows the desired source PR checklist: every source PR is referenced by number and title, and completed integrations are checked off.

Use this layout:
```markdown
## Summary

<Concise lead-in explaining the integrated sweep and why the changes belong together.>

### <Changed area>
- <Concrete behavior, hardening, or test coverage integrated from the source PRs.>
- <Another concrete change, grouped with related work rather than listed by PR number.>

Checklist:
- [x] #123 Short title
- [ ] #124 Short title

## Validation

- `<command that was run>`
```

Rules:
- `## Summary` must describe the actual integrated changes. Do not make it a flat copy of the checklist.
- Use grouped bullets or subsections when the sweep spans multiple areas.
- The checklist must appear immediately after the summary and must include every source PR in the ledger.
- Check off only source PRs that have already been merged, pushed, and closed.
- Add or update `## Validation` after the checklist once local or CI validation has been run.
- Do not manually write CodeRabbit's auto-generated release-notes block. If CodeRabbit later appends one, leave it alone unless it interferes with the intended body.

## Command Patterns

Fetch and merge a source PR:
```bash
git fetch origin pull/<source-pr>/head:codex/tmp-pr-<source-pr>
git merge --no-ff codex/tmp-pr-<source-pr> \
  -m "Merge sweep fixed PR #<source-pr>" \
  -m "Integrate <short fix description> from #<source-pr> into the aggregate sweep branch so <why>."
```

Create the aggregate PR body with a temporary Markdown file:
```bash
body_file=$(mktemp "${TMPDIR:-/tmp}/aggregate-pr-body.XXXXXX.md")
cat > "$body_file" <<'EOF'
## Summary

<Concise lead-in explaining the integrated sweep and why the changes belong together.>

### <Changed area>
- <Concrete behavior, hardening, or test coverage integrated from the source PRs.>
- <Another concrete change, grouped with related work rather than listed by PR number.>

Checklist:
- [x] #123 Short title
- [ ] #124 Short title

## Validation

- `<command that was run>`
EOF

gh pr create \
  --base <base-branch> \
  --head <integration-branch> \
  --title "<aggregate PR title>" \
  --body-file "$body_file"
rm "$body_file"
```

Update the aggregate body with a temporary Markdown file:
```bash
body_file=$(mktemp "${TMPDIR:-/tmp}/aggregate-pr-body.XXXXXX.md")
gh pr view <aggregate-pr> --json body --jq '.body' > "$body_file"
perl -0pi -e 's/- \[ \] #<source-pr> /- [x] #<source-pr> /' "$body_file"
gh pr edit <aggregate-pr> --body-file "$body_file"
rm "$body_file"
```

Close an integrated source PR:
```bash
gh pr close <source-pr> --comment "Integrated into #<aggregate-pr>."
```

List unresolved review threads for an aggregate PR:
```bash
gh api graphql \
  -f owner=<owner> -f repo=<repo> -F number=<aggregate-pr> \
  -f query='
query($owner:String!, $repo:String!, $number:Int!) {
  repository(owner:$owner, name:$repo) {
    pullRequest(number:$number) {
      reviewThreads(first:100) {
        nodes {
          id
          isResolved
          isOutdated
          path
          line
          comments(first:20) {
            nodes { author { login } body url }
          }
        }
      }
    }
  }
}'
```

Resolve a review thread after fixing or replying:
```bash
gh api graphql \
  -f threadId=<review-thread-id> \
  -f query='mutation($threadId:ID!) { resolveReviewThread(input:{threadId:$threadId}) { thread { id isResolved } } }'
```

## Completion Criteria

The task is not done when the branch merely builds locally. It is done only when every source PR has been integrated, pushed, closed, and checked off; CodeRabbit findings have been fixed or answered and resolved; the aggregate PR has no unchecked source items; all CI checks are green; and the branch is clean and pushed.
