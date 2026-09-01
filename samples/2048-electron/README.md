# DeltaXAML Snake sample

This sample is the in-place DeltaXAML port of the source project copied into
this folder. The source folder was named `2048-electron`, but the checked-in
application is Snake (`SnakeGame`, a 24x18 board), matching the supplied
`MainWindow.axaml` and its original behavior.

The XAML file is compiled by the DeltaXAML source generator into
`SnakeArtifact`. The host owns only game state and input decisions; the
retained visual tree, layout and display-list extraction remain in DeltaXAML.
The renderer sample copies the canonical clip-aware solid, rounded and
rounded-slice UI SPIR-V artifacts from DeltaShader and supplies them to
`UiDisplayListGraphFeature`; DeltaXAML does not own or resolve these shaders.

Run the windowed sample with:

```bash
dotnet run --project samples/2048-electron/DeltaXAML.Samples.Snake.csproj \
  -c Release --no-restore -- --frames 600
```

Enable window profiling with `--profiling` (the legacy `--profile` spelling is
also accepted). Completed frame profiles are printed by the renderer profiler:

```bash
dotnet run --project samples/2048-electron/DeltaXAML.Samples.Snake.csproj \
  -c Release --no-restore -- --frames 600 --profiling
```

Run the headless checks with:

```bash
dotnet run --project samples/2048-electron/DeltaXAML.Samples.Snake.csproj \
  -c Release --no-restore -- --headless --frames 600 --slots 16
```

For a repeatable headless performance report with raw per-frame data, warm-up
skipping and spike-resistant medians, run:

```bash
./eng/profile-snake-headless.sh --frames 1000 --skip 100 --slots 16
```

The command writes a raw TSV profile and a machine-readable JSON summary to
`/tmp`. The summary reports filtered and unfiltered medians, robust median
error, MAD deviation and spike counts for frame/pass timings and render
counters; it does not use p95.

The headless mode uses the same Vulkan RenderGraph UI/text path as the windowed
sample, but renders offscreen and prints timing summaries for the last 100
completed profiles. It writes `/tmp/delta-snake-layout.json`. To create a neutral
SVG layout debug view with translucent bounds, clips and text labels:

```bash
python3 tools/layout-json-to-svg.py \
  /tmp/delta-snake-layout.json /tmp/delta-snake-layout.svg
```
