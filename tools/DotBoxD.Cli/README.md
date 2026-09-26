# DotBoxD local tools

Local-only inspection and deterministic offline replay. Build with the solution, then invoke
`dotnet run --project tools/DotBoxD.Cli -c Release -- <command>` (or the built `dotboxd` executable).

- `explain <module.json|package.json|execution.dbxtrace> [--json]`: inspect exported IR, capabilities,
  host bindings and cost metadata. Raw modules use the default pure-binding catalog and deny-by-default
  policy. A trace carries the actual policy and host-binding signatures, allowing full host inspection.
- `replay <execution.dbxtrace> [--backend interpreter|compiled|auto] [--json]`: execute recorded IR
  using only recorded boundary results. Defaults to the interpreter. Divergence is a failure.
- `--help`, `--version`: discovery without executing code.

Flags override defaults; no environment/config files alter behavior. Input paths are mandatory and
relative to the current working directory. No stdin, prompts, network calls, assembly loading or
mutating commands exist, so confirmation, `--yes` and `--dry-run` do not apply. Neither command modifies
its input. Output is plain text (no ANSI); capability/binding lists use stable labeled columns.

`--json` selects an envelope with `ok`, `data`, `error` and `meta.schemaVersion = 1` on stdout,
including expected failures. Human errors go to stderr. Exit 0 means success/matching replay, 1 means
invalid inspection or divergence, and 2 means a usage/input error. Unknown/repeated flags fail.
Future breaking JSON changes require a new schema version. Trace files may contain secrets; use
`ExecutionTrace.Redact` before sharing. A redacted recording cannot silently pass exact replay.

Regression coverage includes help, missing/unknown arguments, human-vs-JSON inspection, invalid input,
and interpreter/compiler replay in the kernel test suite.
