# Mutation Testing

Mutation testing uses the pinned local `dotnet-stryker` tool from `.config/dotnet-tools.json`.
The workflow in `.github/workflows/mutation-tests.yml` runs weekly, through manual dispatch, and on
pull requests labeled `run-mutation-tests`.

Restore tools before running locally:

```bash
dotnet tool restore
dotnet restore DotBoxD.slnx
```

Run the focused kernel validation scope from the project-under-test directory:

```bash
cd src/Kernels/DotBoxD.Kernels.Validation
dotnet tool run dotnet-stryker -- \
  --config-file ../../../.config/stryker/kernels-validation.json \
  --output ../../../artifacts/mutation/kernels-validation \
  --skip-version-check
```

Run the services protocol/framing scope:

```bash
cd src/Services/DotBoxD.Services
dotnet tool run dotnet-stryker -- \
  --config-file ../../../.config/stryker/services-protocol.json \
  --output ../../../artifacts/mutation/services-protocol \
  --skip-version-check
```

Initial local baselines recorded on 2026-07-05 with Stryker.NET 4.16.0:

| Scope | Mutants tested | Killed | Survived | Timeout | Killed/tested | Stryker score |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Kernel validation policy scope | 111 | 71 | 40 | 0 | 63.96% | 30.60% |
| Services protocol/framing scope | 289 | 220 | 57 | 12 | 76.12% | 75.82% |

`Killed/tested` is the raw killed mutant count divided by the table's tested count. `Stryker score`
is the tool-reported mutation score from the same run and remains the value used as the baseline
signal.

The first phase is informational: both configs set `thresholds.break` to `0`, so low mutation
scores do not fail the workflow. Tool crashes and invalid configs should still fail. Once the
scheduled runs are stable, raise `thresholds.break` conservatively and ratchet from there.
