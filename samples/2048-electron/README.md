# DeltaXAML Snake sample

This sample is the in-place DeltaXAML port of the source project copied into
this folder. The source folder was named `2048-electron`, but the checked-in
application is Snake (`SnakeGame`, a 24x18 board), matching the supplied
`MainWindow.axaml` and its original behavior.

The XAML file is compiled by the DeltaXAML source generator into
`SnakeArtifact`. The host owns only game state and input decisions; the
retained visual tree, layout and display-list extraction remain in DeltaXAML.

Run the windowed sample with:

```bash
dotnet run --project samples/2048-electron/DeltaXAML.Samples.Snake.csproj \
  -c Release --no-restore -- --frames 600
```

Run the headless checks with:

```bash
dotnet run --project samples/2048-electron/DeltaXAML.Samples.Snake.csproj \
  -c Release --no-restore -- --headless
```

The headless run writes `/tmp/delta-snake-layout.json`. To create a neutral
SVG layout debug view with translucent bounds, clips and text labels:

```bash
python3 tools/layout-json-to-svg.py \
  /tmp/delta-snake-layout.json /tmp/delta-snake-layout.svg
```
