# Mergeable IR Fragments

## Status

Experimental design and generator slice. This does not replace or wire into the current hook-chain
`Where`/`Select`/`Run` pipeline.

## Problem

The current hook-chain generator lowers a whole fluent expression from the terminal call. It starts at
`Run`, `RunLocal`, `Register`, or `RegisterLocal`, walks backward through known `Where` and `Select`
method names, and emits one complete `PluginPackage`.

That works for the built-in fluent API, but it does not give consumers a way to author their own stage
methods. A custom method between `Where` and `Run` breaks the chain walker unless the analyzer learns
that exact method name and semantics.

## Model

Custom stages should lower one marked delegate argument into a mergeable IR step:

```csharp
public Pipeline<T> Where(
    [LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
    => throw new NotSupportedException();

public Pipeline<T> Where(LoweredPipelineStep step)
    => Append(step);
```

The generator intercepts the `Func` overload at the call site and redirects to the step overload:

```csharp
pipeline.Where(e => e.Distance <= 4);
```

becomes:

```csharp
pipeline.Where(GeneratedStep.Create());
```

Each generated step contains:

- the step kind (`Filter` or `Projection`);
- a single current-value placeholder parameter named `$dotboxd.current`;
- a lowered `Expression` over that placeholder;
- input and output manifest shape tags;
- required capabilities and effects collected during lowering.

The important point is that a step is not a complete `SandboxModule`. A later composer merges steps into a
complete module by rebinding `$dotboxd.current`, allocating temps, and building final `ShouldHandle` and
`Handle` functions.

## Merge Rules

A runtime or generated composer should process steps in order:

1. `Filter`: evaluate the step expression as `bool`; a false result exits `ShouldHandle`.
2. `Projection`: assign the step expression to a new current-value temp and update the current shape.
3. Terminal: build the final `Handle` body from the accumulated current value.

The composer is responsible for validating that each step input shape matches the previous step output
shape. It must also rewrite placeholder variable names to scoped variables in the final module.

## Why Not Per-Step Modules

A per-step `SandboxModule` would be expensive and awkward to merge. It would force each `Where` or `Select`
to pretend it has entrypoints, policy, live settings, and final terminal behavior. The fragment model keeps
the public primitive small: one expression plus metadata.

## Current Implementation Slice

The experimental generator recognizes calls where:

- exactly one method parameter is marked with `[LowerToIr(...)]`;
- the marked parameter is `Func<TInput, bool>` for filters or `Func<TInput, TOutput>` for projections;
- the argument is an expression-bodied lambda with one parameter;
- the receiver type has an overload with the same method name that accepts `LoweredPipelineStep`.

It emits generated step factory classes plus interceptors. It intentionally does not annotate or alter the
existing DotBoxD hook pipeline methods.

Unsupported in this first slice:

- captured locals;
- multi-parameter forwarding;
- extension-method receivers;
- context parameters and host-service selectors;
- final module composition.

Those are separate design steps because each changes the trust boundary or overload shape.

