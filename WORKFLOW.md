# DeltaXAML workflow

```bash
dotnet restore DeltaXAML.slnx
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  dotnet build DeltaXAML.slnx -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false -v:minimal
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  dotnet run --project tests/DeltaXAML.Tests/DeltaXAML.Tests.csproj \
  -c Release --no-build
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  dotnet run --project samples/TipCalc/DeltaXAML.Samples.TipCalc.csproj \
  -c Release --no-build --no-restore
```

The headless harness references DeltaText and therefore SixLabors.Fonts. Local
Furnace checkouts keep the license outside Git at
`../Furnace/Licenses/SixLabors.lic`; CI supplies `SixLaborsLicenseFile` from its
secret. The code-metrics wrapper recognizes the local workspace location when
the variable is not already set. Package vulnerability auditing remains
enabled.

## Repository layout (mandatory)

Every DeltaXAML checkout must keep the same top-level shape, even when one of
the domains is currently empty:

```text
DeltaXAML/
├── src/
├── tests/
├── benchmarks/
├── samples/
├── probes/
├── playground/
├── tools/
├── adr/
├── docs/
├── eng/
├── artifacts/
└── assets/
```

The canonical production project is `src/DeltaXAML/`. Every additional source
project is a sibling named `src/DeltaXAML.<Area>/` (`.Contract`, `.Compiler`,
`.Generator`, and so on). Do not add source projects directly at the repository
root or use an unrelated `src/<name>` directory. Tests, benchmarks, playground
code and helper tools stay in their matching top-level directory; they are not
runtime contracts. `samples/` contains runnable user-facing examples and
vertical slices. `probes/` contains small headless/compiler/contract checks for
one behavior or capability; probes are diagnostics, not substitute samples.
Generated output belongs under `artifacts/`, while checked-in fixtures and
source resources belong under `assets/`.

The layout is enforced by a bounded, dependency-free gate:

```bash
./eng/check-layout.sh
```

The gate fails with the missing or invalid path when a required directory is
absent, `src/DeltaXAML` is missing, or a source sibling does not use the
`DeltaXAML.<Area>` naming form. Empty domains are represented by a tracked
`.gitkeep`; do not remove a required directory because it has no implementation
yet. Run this gate from the repository root before handing work to another
project or changing the solution structure.

Prefer headless layout/input tests before the editor native smoke. Geometry
tests must cover original and resized viewports, clips and stable backing-array
reuse. The real window command lives in
[../DeltaEditor/WORKFLOW.md](../DeltaEditor/WORKFLOW.md).

DeltaShader owns generated shader outputs. Run
`./eng/check-shader-output-ownership.sh` to reject shader binaries and
sidecars in DeltaXAML projects and samples.

The render-backed Snake sample links the canonical producer outputs directly;
it must not use shader copies from another sample. Check the eight linked
UI/text artifacts and their ABI sidecars before building the sample:

```bash
./eng/check-snake-shader-artifacts.sh
```

The check validates the project links, SPIR-V headers, available `spirv-val`
validation, and the set/binding/stride fields consumed by the generated UI and
text shader programs. Set `DELTA_SHADER_ROOT` only when the sibling
DeltaShader checkout is elsewhere.

## Descriptor/mixin architecture (mandatory)

Read [docs/INTERNAL.md](docs/INTERNAL.md) before changing retained controls, properties,
bindings, layout, input or visual extraction. The hard rule is:

> An element class owns identity and composite state. A generic interface
> describes one capability. A stateless `readonly struct` implements the
> algorithm. Generated code binds the concrete element, state and algorithms.

Use the required locations:

- `src/DeltaXAML/Internal/State/*State.cs`: fields and constants only;
- `src/DeltaXAML/Internal/Mixins/*Mixin.cs`: stateless algorithms implementing
  static generic capability interfaces;
- `src/DeltaXAML/Internal/Descriptors/`: immutable operation/property metadata
  and generated thunks;
- `src/DeltaXAML/UserApi/Controls/Ui*.cs`: state, constructors and public
  property/event accessors only.

Do not place algorithms in default interface methods, use control inheritance
to share behavior, discover state through `Type`, store runtime values in
`Dictionary<Type, object>`, or require user controls to be `partial`. Generated
code resolves element/state/mixin combinations at compile time. Runtime layout,
input and visual paths may perform one descriptor dispatch per element/stage,
then operate on typed state by `ref`.

The architecture gate in `DeltaXAML.Tests` is mandatory and must reject:

- control domain/helper methods and algorithm inheritance;
- state methods, service ownership and allocation behavior;
- mixins with instance storage;
- object-valued or type-keyed runtime stores;
- reflection, LINQ or string property lookup in frame stages;
- per-control behavior delegates/subscriptions;
- new references to superseded APIs;
- invalid `State`, `Mixins`, `Descriptors` and `Controls` locations.

Full-capability work additionally extends the gate to reject:

- boxed collection items and per-item template delegates in realization;
- per-element trigger, behavior or gesture objects/subscriptions;
- steady-state ancestor walks for relative bindings;
- object/type-keyed attached-property storage in retained stages;
- full collection rebuilds for bounded collection deltas;
- duplicate popup documents, input routers or property engines;
- rich-text reshaping caused only by color/paint changes;
- platform accessibility, image-decoder or GPU objects in retained state;
- sample fixtures that bypass generated XAML by constructing an equivalent
  host tree in code.

If the gate cannot express a rule through assembly inspection, add a bounded
source/project check instead of weakening the rule.

Missing or empty designated folders are reported as `PENDING` and fail the
headless command. The canonical retained owner is checked by bounded runtime
source rules but is not misclassified as a public control surface.

## Migration completeness (mandatory)

A migration must classify every replaced API, implementation, test and
benchmark. Remove it in the same change whenever possible. If temporary
retention is necessary, mark it `[Obsolete]` immediately with the replacement
and removal milestone:

```csharp
[Obsolete(
    "Use generated UiTypeDescriptor operations; remove during DXAML-MIXIN-4.",
    error: true)]
internal void RetiredMeasure()
{
}
```

Use `error: true` when no supported caller may remain. A warning is allowed
only for a bounded migration step that must compile while consumers move.
New production code, tests and benchmarks may not call obsolete paths. Do not
optimize, extend or document superseded APIs as alternatives. The migration
is not complete while an unmarked old path remains.

Before handing off a migration, report:

1. removed old files and symbols;
2. retained obsolete symbols and their removal milestone;
3. callers still using each retained symbol;
4. architecture-gate result;
5. whether the work still maintains exactly one retained tree/property model.

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

NuGet `NU1900` is an infrastructure advisory, not a DeltaXAML source
diagnostic: it is emitted when the restore cannot reach the package
vulnerability feed. A successful restore with feed access should clear it; when
the feed is unavailable, record the count and keep it separate from analyzer
warnings. Do not disable package auditing merely to hide a feed outage.

Compact byte enums are explicitly allowed by the repository `.editorconfig`:
`CA1028` is disabled so ABI and metadata enums do not need repetitive
per-declaration suppressions. Use `byte` only where the compact representation
is deliberate (for example frozen packet/visual metadata); new enums that are
not size-sensitive should keep the default `int` representation.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. The script uses `dotnet format whitespace
--folder` intentionally: it avoids the MSBuild/Roslyn workspace load that can
hang on macOS with the .NET 10 SDK. It therefore checks/applies whitespace
formatting only; analyzer/style diagnostics remain covered by the build and
SARIF metrics workflow. `FORMAT_RESTORE` is no longer needed for this command.
