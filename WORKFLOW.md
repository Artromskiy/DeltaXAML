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

Run the manual GitHub Actions `Code metrics` workflow when maintainability
evidence is needed. It enables CA1501/CA1502/CA1505/CA1506 as report-only
diagnostics and uploads the SARIF, build log and exit summary as artifacts.
