# DeltaXAML workflow

```bash
dotnet restore DeltaXAML.slnx
dotnet build DeltaXAML.slnx -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
dotnet run --project tests/DeltaXAML.Core.Tests/DeltaXAML.Core.Tests.csproj \
  -c Release --no-build
```

Prefer headless layout/input tests before the editor native smoke. Geometry
tests must cover original and resized viewports, clips and stable backing-array
reuse. The real window command lives in
[../DeltaEditor/WORKFLOW.md](../DeltaEditor/WORKFLOW.md).

## Code metrics

Run the same analyzer/code-metrics build locally and in the manual GitHub
Actions workflow through the repository wrapper:

```bash
./eng/code-metrics.sh -v:q
```

`eng/code-metrics.sh` converts `CODE_METRICS_ERROR_LOG` (default:
`artifacts/code-metrics/diagnostics.sarif`) to an absolute path before
MSBuild starts, so multi-project builds write one repository-level SARIF
instead of resolving a missing directory relative to each project. An
explicit destination is supported:

```bash
CODE_METRICS_ERROR_LOG=/tmp/code-metrics.sarif ./eng/code-metrics.sh -v:q
```

Inspect the SARIF and summary artifacts from the manual workflow. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. The script uses `dotnet format whitespace
--folder` intentionally: it avoids the MSBuild/Roslyn workspace load that can
hang on macOS with the .NET 10 SDK. It therefore checks/applies whitespace
formatting only; analyzer/style diagnostics remain covered by the build and
SARIF metrics workflow. `FORMAT_RESTORE` is no longer needed for this command.
