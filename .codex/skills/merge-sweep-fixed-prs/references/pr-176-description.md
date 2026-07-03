# JKamsker/DotBoxD#176 PR Description Reference

Source: https://github.com/JKamsker/DotBoxD/pull/176

This body is the reference for the desired aggregate source-PR ledger: every source PR is referenced by number and title, and completed integrations are checked off.

```markdown
Combines the surprise-hunt red-test PRs into one reviewable integration branch.

Checklist:
- [x] #151 BindingRegistry rejects undefined enum values
- [x] #152 QueryFilter rejects invalid kinds
- [x] #153 Direct HTTP grants use defaults
- [x] #164 Event property capability can be omitted from requiredCapabilities
- [x] #154 Compiled reflection caches honor pre-canceled callers
- [x] #155 HookResult builder handles keyword-escaped namespaces
- [x] #156 Successful RPC envelopes reject error fields
- [x] #157 Wildcard capability revocation stops wildcard-authorized plans
- [x] #158 QueryFilter rejects invalid comparison operators
- [x] #159 Async live-setting flush honors pre-canceled tokens
- [x] #160 file.writeText denial reports write operation
- [x] #161 QueryFilter Not rejects empty children cleanly
- [x] #162 Hook publish honors pre-canceled local handlers
- [x] #163 Error RPC envelopes reject stream handles
- [x] #170 RPC envelopes reject deeply nested unknown fields
- [x] #171 QueryFilter rejects empty leaf field paths
- [x] #172 Hook result dispatch honors pre-canceled no-handler paths


<!-- This is an auto-generated comment: release notes by coderabbit.ai -->
## Summary by CodeRabbit

* **Bug Fixes**
  * Fail-fast validation for bindings, query filters (kind/operator/shape), and RPC MessagePack response/request envelope invariants with clearer error codes.
  * Safer grant validation: HTTP and timeouts are only validated when provided.
  * Wildcard capability revocations enforced; file-extension permission-denied messaging is now operation-specific.
* **New Features**
  * Plugin capability-grant validation now considers module-required non-binding capabilities.
* **Tests**
  * Expanded validation, cancellation, plugin-policy, file-extension, and streaming/RPC envelope regression coverage.
* **Chores**
  * Improved generated hint-name sanitization.
<!-- end of auto-generated comment: release notes by coderabbit.ai -->
```
