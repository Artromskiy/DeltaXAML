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

Run the manual GitHub Actions `Code metrics` workflow before committing a
substantial change, then inspect its SARIF and summary artifacts. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. Run the check before committing substantial
changes. The script uses the repository `.editorconfig` and `Directory.Build.props`.
