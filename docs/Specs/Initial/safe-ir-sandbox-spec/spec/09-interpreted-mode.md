# 09 — Interpreted Mode

## Purpose

Interpreted mode executes verified IR directly without compiling to IL or loading a DLL.

This is required for:

- quick one-off executions
- rare plugin hooks
- debugging
- fast iteration
- environments where runtime code generation is unavailable or undesirable
- early MVP before compiled backend is ready

## Core requirement

Interpreted mode and compiled mode must share:

- canonical IR
- type checker
- effect analyzer
- policy resolver
- binding registry
- resource budgets
- audit sink
- error model
- deterministic mode

Only the backend changes.

## Interpreter pipeline

```text
JSON IR
  -> import
  -> canonicalize
  -> validate
  -> type check
  -> effect check
  -> policy resolve
  -> lower to executable bytecode or typed IR
  -> execute in interpreter
```

No assembly is emitted.

## Execution representation

Two possible approaches:

### Option A: Tree-walking interpreter

Executes AST/IR nodes recursively.

Pros:

- easiest MVP
- good diagnostics
- simple stepping

Cons:

- slower
- recursion depth concerns
- harder to optimize

### Option B: Bytecode interpreter

Lower IR to sandbox bytecode:

```text
LoadConst 0
StoreLocal 1
LoadLocal 1
CallBinding file.readText
Return
```

Pros:

- faster
- easier fuel accounting
- easier compiler parity
- easier differential testing

Cons:

- more implementation work

Recommendation:

Start with tree-walking only if speed of implementation matters. Move to bytecode before compiled mode.

## Interpreter state

```csharp
public sealed class InterpreterFrame
{
    public FunctionId Function { get; }
    public SandboxValue[] Locals { get; }
    public int InstructionPointer { get; set; }
}

public sealed class InterpreterState
{
    public SandboxContext Context { get; }
    public Stack<InterpreterFrame> Frames { get; }
    public SandboxValue? ReturnValue { get; set; }
}
```

## Fuel accounting

Interpreter fuel checks are straightforward.

Charge fuel for:

- every instruction or grouped instruction block
- loop backedges
- function calls
- host binding calls
- collection operations
- string/bytes operations

Example:

```csharp
context.ChargeFuel(operation.Cost);
```

Loop backedges should charge extra fuel.

## Binding calls

Interpreter calls binding descriptors directly:

```csharp
var binding = plan.Bindings.Get(bindingSlot);
context.RequireCapability(binding.RequiredCapability);
context.ChargeFuel(binding.CostModel.BaseFuel);
var result = await binding.Interpreter(context, args, ct);
```

The interpreter must not use reflection to invoke arbitrary methods from user-controlled names.

## Debugging support

Interpreted mode should support:

- step over/into/out
- breakpoints by JSON location or IR node ID
- variable inspection
- trace host calls
- trace fuel usage
- deterministic replay when inputs/policy are stable

Debug traces should include:

```text
runId
moduleHash
functionId
instructionId
jsonLocation optional
locals snapshot optional/limited
fuelRemaining
```

## Diagnostics

Interpreter errors should point to IR node IDs or JSON locations where available.

Examples:

```text
E-POLICY-001: capability file.read required by file.readText but not granted.
E-RUNTIME-004: fuel exhausted in function calculateLoot at loop starting line 42.
E-BINDING-007: file.readText denied path outside sandbox root.
```

## When to use interpreted mode

Use interpreted mode when:

```text
estimatedCost < small threshold
runCount < hotness threshold
debugging enabled
cache miss and compile latency not worth it
module uses features not supported by compiler yet
policy forbids dynamic code generation
host runs in constrained environment
```

## Auto mode

Auto mode should begin interpreted and promote to compiled after a threshold.

Suggested heuristic:

```text
if DebugEnabled: Interpreted
else if CompiledCacheHitAndVerified: Compiled
else if EstimatedOps < 10_000 and HistoricalRuns < 20: Interpreted
else CompileAndCacheThenRun
```

Thresholds must be configurable.

## Hotness tracking

Track by canonical execution-plan hash:

```text
planHash
runCount
averageDurationInterpreted
averageFuelUsed
lastRunAt
compileFailures
compiledArtifactHash optional
```

If interpretation becomes expensive, compile.

## Interpreter optimizations

Safe optimizations:

- constant folding during canonicalization
- direct binding slot lookup
- precomputed local indices
- bytecode dispatch table
- small-value structs for primitives
- string interning only if controlled
- specialized opcodes for common operations

Avoid optimizations that change semantics compared to compiled mode.

## Interpreter/compiler parity

Every accepted module should pass differential tests:

```text
interpret(plan, input) == compileAndRun(plan, input)
```

For nondeterministic effects, compare under deterministic injected clock/random/network fixtures.

## Failure behavior

If compiled mode fails because of:

- compiler unavailable
- verifier unavailable
- cache invalid
- unsupported backend feature

The host may fall back to interpreted mode only if:

- the same verified execution plan is used
- policy allows interpreted fallback
- audit records fallback reason

Never fall back to a less restrictive validation path.

## Benefits for sandboxing

Interpreted mode is often safer initially because:

- there is no generated IL to verify
- there is no assembly loading
- every operation is under direct runtime control
- fuel checks are trivial
- host calls are centralized

For a first production version, interpreted mode can be the default and compiled mode can be an optimization.
