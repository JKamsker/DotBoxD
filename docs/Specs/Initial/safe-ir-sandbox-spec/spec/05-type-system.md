# 05 — Type System

## Type-system goals

The sandbox type system must prevent accidental exposure of CLR power.

It should be:

- explicit
- closed
- serializable
- easy to validate
- independent from CLR type names
- mappable to interpreter values and compiled stubs

## Allowed primitive types

Initial primitive types:

```text
Unit
Bool
I32
I64
F32
F64
Decimal optional
String
Bytes optional
```

Notes:

- `String` is a sandbox string value, not permission to use arbitrary `System.String` APIs.
- `Bytes` should be quota-controlled and immutable unless using safe buffers.
- `Decimal` is useful for money/economy logic but can be added later.

## Composite types

```text
Option<T>
Result<TOk, TError>
List<T>
Map<TKey, TValue>
Record { field: Type, ... }
Tuple<T1, T2, ...> optional
```

Restrictions:

- `TKey` for maps must be hashable in sandbox semantics.
- collection sizes are budgeted.
- no arbitrary `IEnumerable<T>` from CLR.
- no arbitrary LINQ.

## Domain/reference types

Use opaque handles/IDs, not host objects.

Examples:

```text
PlayerId
ItemId
QuestId
MapId
EntityRef<TKind>
SandboxPath
SandboxUri
```

A `PlayerId` is data. It is not a `Player` object with methods.

## Forbidden types

Never expose these to IR:

```text
Object
Dynamic
Type
Assembly
MemberInfo
MethodInfo
PropertyInfo
FieldInfo
ConstructorInfo
Module
RuntimeTypeHandle
RuntimeMethodHandle
RuntimeFieldHandle
Delegate
Expression
IQueryable
IServiceProvider
ServiceProvider
Stream
TextReader
TextWriter
FileInfo
DirectoryInfo
DriveInfo
HttpClient
Socket
DbConnection
DbContext
Process
Thread
Task
CancellationTokenSource
IntPtr
UIntPtr
SafeHandle
Span<T>
Memory<T>
ref struct
Pointer<T>
```

`CancellationToken` may be accepted only inside trusted runtime internals, not as a user-visible IR value.

## Type identity

Type identity should be sandbox-native:

```text
TypeId = namespace + name + sandbox version
```

Do not use CLR assembly-qualified type names as sandbox type IDs.

## CLR mapping

Interpreter mapping:

```text
SandboxType -> SandboxValue representation
```

Compiled mapping:

```text
SandboxType -> runtime facade structs/classes known to compiler/verifier
```

The generated code should not be free to choose CLR types. The compiler chooses the representation.

## Recommended value representation

For interpreter:

```csharp
public abstract record SandboxValue;
public sealed record BoolValue(bool Value) : SandboxValue;
public sealed record I32Value(int Value) : SandboxValue;
public sealed record StringValue(string Value) : SandboxValue;
public sealed record ListValue(SandboxList Value) : SandboxValue;
```

For compiled mode, two options:

### Option A: boxed sandbox values

Generated code operates on `SandboxValue`.

Pros:

- simple
- verifier allowlist small
- easy parity with interpreter

Cons:

- slower
- more allocations

### Option B: typed generated methods

Generated code uses primitive CLR types internally and converts at boundaries.

Pros:

- faster
- less overhead

Cons:

- compiler/verifier more complex
- more surface area

Recommendation:

Start with Option A. Add Option B only after semantic and security tests are mature.

## No implicit object conversion

There must be no implicit conversion to/from `object` in the IR.

Bad:

```text
hostCall invoke(name: String, args: List<Object>) -> Object
```

Good:

```text
hostCall game.getHealth(player: PlayerId) -> I32
hostCall file.readText(path: SandboxPath) -> String
```

## Strings

String operations should be explicit and budgeted.

Allowed operations:

```text
string.length
string.isEmpty
string.substringBudgeted
string.concatBudgeted
string.equals
string.compareOrdinal
string.toLowerInvariant optional
string.toUpperInvariant optional
```

Potentially dangerous/expensive operations:

- regex
- culture-sensitive casing/comparison
- normalization
- format strings
- interpolation with large values

Add them as safe bindings with costs and limits.

## Collections

Collections are sandbox-owned.

Required limits:

- max collection count
- max nested depth
- max total elements per run
- max bytes for strings/bytes inside collections

Collection operations charge:

- CPU fuel
- allocation budget for growth
- audit/event optional for large growth

## Domain objects

Do not expose rich mutable domain objects.

Bad:

```csharp
public Player GetPlayer(int id); // returns host Player object
```

Good:

```csharp
public PlayerSnapshot GetPlayerSnapshot(PlayerId id);
public void EnqueueInventoryCommand(PlayerId id, ItemId item, int amount);
```

Better for mutations:

```text
IR emits command -> host validates transaction -> host applies after sandbox returns
```

This prevents user logic from directly mutating live game/server objects.

## Type validation rules

Reject a module if:

- any type ID is unknown
- any generic arity is wrong
- any nested type exceeds max depth
- a type is marked host-internal
- a function exposes a forbidden type
- a binding exposes a forbidden type
- a conversion is not explicitly defined
- a nullable-like value is used without `Option<T>` or equivalent

## Versioning

Types should have stable semantic versions.

Breaking type changes require:

- new type ID version, or
- module target version bump, or
- compatibility adapter

Do not silently reinterpret cached IR under a changed type definition.
