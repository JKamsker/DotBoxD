# EventQuery vs. the source-generated pipeline

## Status

Reference. Explains why `DotBoxD.Queryable`'s `EventQuery<TEvent>` is deliberately **not** folded into the
`[PipelineSurface]` plus explicit `IRBodyOf` companion vocabulary that drives the hook-chain and mergeable-IR
source generator, and when to reach for each.

## Two event models, on purpose

DotBoxD has two ways to react to events. They look superficially similar - both offer `Where` and `Select`
and a terminal - but they sit on opposite sides of the trust and execution boundary, and unifying their
authoring surface would break one of them.

| | Source-generated pipeline | `EventQuery<TEvent>` |
|---|---|---|
| Namespace | `DotBoxD.Plugins.Runtime` (hooks/subscriptions) + `[IRBodyOf]` mergeable-IR | `DotBoxD.Queryable.Authoring` |
| Operator input | **source lambdas** - `Func<T, bool>` / `Func<T, U>` whose body the analyzer reads at compile time | **runtime expression trees** - `Expression<Func<T, bool>>` / `Expression<Func<T, U>>` |
| Recognition | analyzer, by `[PipelineSurface]` on the fluent type plus standard method names whose delegate is paired with an explicit `[IRBodyOf]` `IRFunc`/`IRKernel` companion (`[LowerToIr]` remains legacy for mergeable-IR) | none - the analyzer never sees it |
| Lowering | compile time, into verified sandbox IR (`SandboxModule` / `LoweredPipelineStep`) | runtime, into a portable `QueryFilter` / `QueryProjection` AST |
| Execution | inside the sandbox (interpreter or compiled kernel), fuel- and capability-gated | in-process tree-walking interpreter with a JIT-compile promotion step |
| Terminal | `Run` / `RunLocal` / `Register` / `RegisterLocal`, lowered into the module | `SubscribeAsync`, dispatching to a native in-process delegate |
| Trust | designed for **untrusted plugin code** - the server re-verifies the IR | **host-trusted** code building its own subscriptions |

## Why source-generated pipeline contracts do not apply to `EventQuery`

The explicit companion vocabulary is a contract with the **source generator**, and the generator only lowers
source-level lambda syntax at a call site. `EventQuery.Where`/`Select` take `Expression<Func<…>>`: the C#
compiler materializes those as runtime expression-tree objects, which is exactly what `EventQuery` wants -
it needs the tree at runtime to conjoin predicates, decide indexing, and JIT-promote hot queries. There is
no `Func<>` delegate parameter paired with an `IRFunc`/`IRKernel` companion for the generator to fill, so
marking the type as a pipeline surface would be inert. More importantly, lowering those expressions to a static module at
compile time would **remove** the runtime dynamism (captured locals, runtime-varying predicates) that is the
entire reason `EventQuery` exists.

So this is a category difference, not a missing annotation. The shared *vocabulary* is the role set
(filter / projection / terminal); the *mechanisms* are intentionally distinct.

## When to use which

- **Authoring plugin logic that runs in the sandbox** (untrusted, verified, portable): use the fluent hook /
  subscription pipeline, or a custom `[PipelineSurface]` type whose standard `Where`/`Select`/terminal methods
  expose explicit `IRBodyOf` companion parameters. Combine collected `LoweredPipelineStep` fragments with
  `LoweredPipelineComposer` when you need the runtime counterpart to the generator's build-time fusion.
- **Building a dynamic, host-side subscription** whose predicate is only known at runtime (built from user
  input, config, or captured state): use `EventQuery<TEvent>`.

## A possible future bridge (not built)

`EventQuery`'s portable `QueryFilter` / `QueryProjection` AST and the pipeline's `LoweredPipelineStep` are both
small filter/projection IRs. A runtime translator from the former to the latter could, in principle, feed
`LoweredPipelineComposer` and give `EventQuery` an opt-in *sandboxed* execution mode. That is a separate
feature - it trades away dynamism for sandboxing - and is deliberately out of scope here.
