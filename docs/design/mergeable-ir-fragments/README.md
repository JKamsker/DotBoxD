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

Custom stages should expose a delegate plus an optional generated IR companion:

```csharp
public Pipeline<T> Where(
    Func<T, bool> predicate,
    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
{
    ArgumentNullException.ThrowIfNull(irPredicate);
    return Append(irPredicate.Step);
}
```

The generator intercepts the call site and supplies the companion argument:

```csharp
pipeline.Where(e => e.Distance <= 4);
```

becomes:

```csharp
pipeline.Where(e => e.Distance <= 4, GeneratedStep.CreateIRFunc());
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

## Composition (implemented)

`LoweredPipelineComposer.Compose(LoweredPipelineComposition)` in `DotBoxD.Abstractions` merges an ordered
`LoweredPipelineStep` list into one verifiable `SandboxModule` with two entrypoints over the input record:

- `ShouldHandle(input) -> Bool` threads the value through the chain, returning `false` as soon as any filter
  fails; projections rebind the running value so later filters see the projected shape.
- `Handle(input) -> ResultType` applies the projections in order (filters are already gated) and returns the
  final projected value.

Each fragment's `$dotboxd.current` placeholder is rewritten to the scoped variable holding the value flowing
into that step, input/output shape tags are validated to chain, and the union of the steps' required
capabilities and effects is surfaced in module metadata. The composed module verifies through the normal
host validator and runs on the interpreter, so a consumer that collected steps from a custom pipeline surface
can hand-assemble exactly what the build-time hook-chain fusion would have produced.

## Runtime Builder (implemented)

For dynamic or advanced scenarios, use the public runtime builder instead of copying the generator's raw
`LoweredPipelineStep` construction:

```csharp
using DotBoxD.Plugins;

var maxDistance = 4;
var ir = IRBuilder.For<MonsterAggroEvent>();
var steps = new[]
{
    ir.FilterStep(e => e.LessThanOrEqual(e.Field(2), e.Int32(maxDistance))),
    ir.ProjectionStep<string>(e => e.Field(0))
};

var filter = ir.Filter(e => e.LessThanOrEqual(e.Field(2), e.Int32(maxDistance)));
pipeline.Where(e => e.Distance <= maxDistance, filter);
```

`IRBuilder.For<TInput>()` validates `TInput` through the same RPC marshaller used by plugin runtime
adapters, emits generator-compatible manifest shape tags, and creates the single `$dotboxd.current`
placeholder parameter required by `LoweredPipelineComposer`. `IRExpressionBuilder.Field(index)` reads the
marshalled record order: public readable properties first, then public fields.

Step metadata is still explicit at the call site:

```csharp
var filter = ir.FilterStep(
    e => e.LessThanOrEqual(e.Field(2), e.Int32(maxDistance)),
    requiredCapabilities: ["world.monsters.read"],
    effects: ["Cpu"]);
```

The lower-level `Expression` and `LoweredPipelineStep` records remain public for importers and tooling that
already have a complete IR tree, but ordinary runtime-authored hook fragments should prefer the builder.

## Why Not Per-Step Modules

A per-step `SandboxModule` would be expensive and awkward to merge. It would force each `Where` or `Select`
to pretend it has entrypoints, policy, live settings, and final terminal behavior. The fragment model keeps
the public primitive small: one expression plus metadata.

## Current Implementation Slice

The experimental generator recognizes calls where:

- exactly one method parameter is marked with `[IRBodyOf(nameof(sourceParameter))]`;
- the marked parameter is an optional-null `IRFunc<TInput, TOutput>` companion;
- the source parameter is `Func<TInput, bool>` for filters or `Func<TInput, TOutput>` for projections;
- the argument is an expression-bodied lambda with one parameter;

It emits generated step factory classes plus interceptors. The older `[LowerToIr(...)]` parameter plus
`LoweredPipelineStep` overload shape remains supported as legacy generator plumbing during migration.

Unsupported in this first slice:

- captured locals;
- multi-parameter forwarding;
- extension-method receivers;
- context parameters and host-service selectors.

Those are separate design steps because each changes the trust boundary or overload shape. Final module
composition is now provided by `LoweredPipelineComposer` (see [Composition](#composition-implemented) above).

For the runtime-dynamic counterpart to this compile-time model — `EventQuery<TEvent>`, which uses runtime
expression trees rather than source-lambda lowering and is deliberately not part of the explicit
`IRBodyOf` companion vocabulary — see [event-query-vs-pipeline](../event-query-vs-pipeline/README.md).
