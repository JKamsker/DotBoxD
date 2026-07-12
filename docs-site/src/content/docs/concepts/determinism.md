---
title: Determinism contract
description: Inputs that participate in deterministic sandbox execution and hashing.
---

Deterministic execution is what makes kernel runs **replayable and auditable**: given the same
inputs, a run produces the same result, audit stream, and resource usage — so you can reproduce a
production incident offline, regression-test plugin behavior, or trust that a cached compiled
artifact corresponds to the module it was built from. This page defines exactly which inputs
participate in that contract. If you never replay or audit kernel runs, you can safely skip it.

Determinism is an explicit policy contract, not a claim that every host environment is reproducible.

## Execution inputs

A deterministic run is defined by the canonical module, entrypoint, input value, policy (including
logical time and random seed), binding manifest and versions, runtime/compiler/verifier versions, and
live-setting snapshot captured for the run. Changing any of those inputs may change the result, audit
stream, resource usage, plan hash, or compiled artifact key.

The host wall clock, process-global random state, current culture, environment variables, filesystem,
and network are excluded unless exposed through a granted binding. Deterministic policies require a
logical clock for time effects and a complete `ulong` seed for random effects. External I/O remains an
input supplied by the host and must be snapshotted by the application when replayability matters.

## Hashes

- The module hash covers canonical IR semantics; JSON property order and insignificant formatting do
  not participate.
- The policy hash covers grants, grant parameters, limits, deterministic inputs, and policy identity.
- The plan hash binds the module and policy to the resolved binding manifest.
- RPC contract fingerprints cover sorted generated wire names and signatures.

Hashes are compatibility identifiers, not authentication tags. Persist the full manifest alongside a
hash and use release signatures/attestations when authenticity matters.

## Drift policy

Interpreter/compiler differential properties and pinned golden cases cover arithmetic, control flow,
file effects, deterministic time/random behavior, audit events, and resource accounting. A deliberate
semantic change updates the golden expectation and changelog in the same review; unexplained drift is
a regression.
