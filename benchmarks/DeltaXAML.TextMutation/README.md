# DeltaXAML text mutation probe

This is a bounded, headless performance probe for the retained text path. It
does not use BenchmarkDotNet and is not part of the normal test harness.

It reports six cases:

- `setter-only`: repeated `UiTextBlock.Text` mutation and invalidation;
- `unchanged pipeline`: warm `Layout` plus `BuildDisplayList` without a text change;
- `text mutation`: the same pipeline while alternating between two existing strings.
- `cached shaped text`: the mutation pipeline with both fixture strings shaped
  once during warm-up and then returned by a control `ITextService`, isolating
  retained DeltaXAML work from the DeltaText shaping implementation.
- `many same-text setters`: 256 retained text elements repeatedly assigned the
  same existing character, isolating property-source overhead at a larger
  element count;
- `many same-text pipeline`: the same 256 elements through warm
  `Layout`/`BuildDisplayList` frames without changing their text.

Pass `--5000-textboxes` for a separate large retained-tree probe. It creates
5000 `UiTextBox` controls containing the same character, reports managed
construction/retained-heap bytes, process private bytes and working set, and
measures repeated same-text setters plus warm layout/display frames. The
process-memory figures include the .NET runtime, JIT and loaded dependencies;
the managed heap delta and current-thread allocation counter are the useful
DeltaXAML-specific comparisons.

The mutation case uses a real `Delta.Text`/SixLabors shaping service and counts
shape calls. The two strings are constants, so the reported allocation excludes
allocation by a caller that creates a new string before assigning `Text`.

Run from the DeltaXAML repository root after the normal restore/build:

```bash
SixLaborsLicenseFile=/path/to/Furnace/Licenses/SixLabors.lic \
  dotnet run --project benchmarks/DeltaXAML.TextMutation/DeltaXAML.TextMutation.csproj \
  -c Release --no-restore
```

Run the 5000-TextBox probe:

```bash
SixLaborsLicenseFile=/path/to/Furnace/Licenses/SixLabors.lic \
  dotnet run --project benchmarks/DeltaXAML.TextMutation/DeltaXAML.TextMutation.csproj \
  -c Release --no-restore -- --5000-textboxes
```

Use this as a repeatable directional probe, not as a statistical benchmark
claim. The source of truth for the retained text cache remains the DeltaXAML
headless tests.
