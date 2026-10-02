# Mutation testing and fuzzing

The normal test suites include deterministic property and mutation-oriented tests:

- `tests/DotBoxD.Services.Tests/Fuzz`: frame parsing against an independent wire oracle,
  all 256 type tags, size/overflow boundaries, fragmented streams, every truncation of
  representative frames and envelopes, duplicate/missing/misspelled/wrong-type fields,
  shuffled fields with nested unknown values, trailing bytes, and Unicode scalar/surrogate cases.
- `tests/DotBoxD.Kernels.Tests/Fuzz/Json`: generated expression trees, canonical export/import
  identity, input corruption, duplicate keys, and exact JSON byte/string/breadth/depth/token limits.

Run the focused tests with:

```sh
dotnet test tests/DotBoxD.Services.Tests -c Release --filter 'FullyQualifiedName~Tests.Fuzz'
dotnet test tests/DotBoxD.Kernels.Tests -c Release --filter 'FullyQualifiedName~Tests.Fuzz.Json'
```

The fixed seeds make failures repeatable. xUnit reports the seed for Services theories;
CsCheck reports a replay seed and shrinks the generated integer seed for JSON properties.
The random generators are bounded; the maximum-frame tests separately exercise the 16 MiB limit.
These tests assert semantics, not just absence of exceptions.

## Stryker.NET

Restore the pinned tools, then run from the production project directory. For example:

```sh
dotnet tool restore
cd src/Kernels/DotBoxD.Kernels.Serialization.Json
dotnet tool run dotnet-stryker -- \
  --config-file ../../../.config/stryker/kernels-json.json \
  --output ../../../artifacts/mutation/kernels-json --skip-version-check
```

The `.config/stryker` configurations cover JSON import budgets/string safety, policy validation,
assembly verification, RPC framing/streaming, and MessagePack envelopes. The mutation workflow
runs weekly, manually, or on PRs bearing `run-mutation-tests`. Reports include surviving,
uncovered, timed-out, and compile-error mutants; inspect those distinctions before interpreting
any score. JSON and MessagePack enforce an 80% floor; the policy and protocol floors remain unchanged.
Run mutation campaigns separately from builds/tests in the same checkout to avoid competing
writes to build outputs. No mutants are excluded simply to make the new tests pass.

## Replay and seed corpus

The standalone harness supports `json`, `verifier`, `framing`, `messagepack-request`, and
`messagepack-response`. It catches only expected parser rejection exceptions. Successful JSON
imports must retain canonical identity across export/import. Successful envelopes must produce
stable serialization and agree between generic and runtime-type decoding. Frame parsing is
checked against an independent wire-format oracle. The verifier must diagnose rejected inputs.

```sh
dotnet publish tools/Fuzzing/DotBoxD.Fuzzing -c Release -o artifacts/fuzz/app
cp -r tools/Fuzzing/corpus artifacts/fuzz/corpus
dotnet artifacts/fuzz/app/DotBoxD.Fuzzing.dll --write-corpus artifacts/fuzz/corpus
dotnet artifacts/fuzz/app/DotBoxD.Fuzzing.dll json --replay artifacts/fuzz/corpus/json
```

`--write-corpus` adds valid request, success/error response, stream-handle, and data/control frame seeds and a real
managed PE for metadata verification. The managed PE is a build-derived seed, not a promise that
this assembly passes the sandbox verifier. Hand-authored JSON seeds exercise arithmetic,
branching, Unicode, and rejection. Replay accepts a directory or one file, rejects empty
corpus directories, prints each input before execution, and exits unsuccessfully on a finding.
PR CI replays all five corpora without instrumentation.

## Coverage-guided fuzzing

Install AFL++ and SharpFuzz.CommandLine 2.3.0. Publish into a fresh directory, generate the corpus
**before** instrumentation, then instrument the library under test (not just the harness):

| Target | Assembly to instrument |
| --- | --- |
| `json` | `DotBoxD.Kernels.Serialization.Json.dll` |
| `verifier` | `DotBoxD.Kernels.Verifier.dll` |
| `framing` | `DotBoxD.Services.dll` |
| `messagepack-request`, `messagepack-response` | `DotBoxD.Codecs.MessagePack.dll` |

```sh
sharpfuzz artifacts/fuzz/app/DotBoxD.Services.dll
mkdir -p artifacts/fuzz/findings
AFL_SKIP_BIN_CHECK=1 AFL_SKIP_CPUFREQ=1 AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1 \
  afl-fuzz -m none -t 5000 -V 300 \
  -i artifacts/fuzz/corpus/framing -o artifacts/fuzz/findings/framing \
  -- dotnet artifacts/fuzz/app/DotBoxD.Fuzzing.dll framing
bash eng/scripts/check-fuzz-findings.sh artifacts/fuzz/findings/framing
```

The harness uses SharpFuzz's out-of-process driver. `AFL_SKIP_BIN_CHECK` allows the managed
launcher; do not use AFL's `-n` mode, which disables coverage guidance. The scheduled/manual CI
campaign runs each target for five minutes, fails for missing/empty run statistics or saved
crashes/hangs, and uploads the raw findings, queue, statistics, and minimized reproducers.
Minimization has a per-input timeout and preserves originals if it cannot finish.

Replay findings with a fresh **uninstrumented** publish and `--replay <file>`. Add a focused
regression test and retain useful minimized inputs in the corpus. A short run with no findings
is evidence only for the inputs explored, not proof of parser safety.
