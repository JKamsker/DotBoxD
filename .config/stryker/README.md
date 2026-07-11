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

Measured local baselines with Stryker.NET 4.16.0:

| Scope | Measured | Mutants tested | Killed | Survived | Timeout | No coverage | Killed/tested | Stryker score | Break threshold |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Kernel validation policy scope | 2026-07-05 | 227 | 217 | 10 | 0 | 5 | 95.59% | 93.53% | 85% |
| Services protocol/framing scope | 2026-07-05 | 305 | 263 | 36 | 6 | 1 | 86.23% | 87.91% | 85% |

`Killed/tested` is the raw killed mutant count divided by the table's tested count. `Stryker score`
is the tool-reported mutation score from the same run and remains the value used as the baseline
signal.

Both focused scopes now have supported `thresholds.break` ratchets of 85. The remaining services
protocol/framing undetected mutants are concentrated in equivalent empty-payload copies, `ConfigureAwait`
continuation flags, defensive disposal paths, and redundant frame-minimum guards that are validated by
later length checks. Tool crashes and invalid configs should still fail.
