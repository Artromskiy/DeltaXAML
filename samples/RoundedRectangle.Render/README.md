# Rounded rectangle renderer sample

This sample loads [`RoundedRectangle.dxaml`](RoundedRectangle.dxaml) through the
DeltaXAML `XamlLoader`, builds one retained `UiDocument`, and submits its
canonical `UiDisplayList` through `DeltaRender.XAML.UiDisplayListGraphFeature`.
The rounded rectangle uses the generated `DeltaShader.UI` rounded-rectangle
artifacts and four independent corner radii.

This sample does not own or copy compiled shaders. Obtain the producer-owned
`ShaderArtifact`/`ShaderAbi`, generated `VertexAbi`/`FragmentAbi` and typed
packers through the `DeltaShader.Tool` NuGet-backed producer build. Pass them
through the normal Render handoff; do not invoke the shader CLI, parse sidecars
or add `.spv` files to this sample.

Run continuously until the window is closed from the DeltaXAML repository root:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64
```

Add `--watch` to reload the source XAML at frame boundaries while the sample
is running; no rebuild is required. The watcher uses the source file under
`samples/RoundedRectangle.Render` when the command is started from the
repository root. A file that is temporarily invalid is rejected and the last
valid document remains active until the next successful save:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64 -- --watch
```

Use `--frames N` for a bounded run. The host requires an SDL3
display, Vulkan/MoltenVK and compatible checked-out DeltaRender/DeltaShader
artifacts. DeltaXAML itself remains renderer-neutral.

For a minimal layout/debug comparison, [`GridTwoRows.dxaml`](GridTwoRows.dxaml)
contains one `Grid` with two 100x100 `Border` squares in rows 0 and 2. Headless
mode can write both the Vulkan readback and the layout diagnostics consumed by
the external SVG tool:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64 --no-build -- --headless --frames 1 \
  --xaml GridTwoRows.dxaml \
  --readback /tmp/delta-grid-two-rows.ppm \
  --layout-json /tmp/delta-grid-two-rows-layout.json

python3 tools/layout-json-to-svg.py \
  /tmp/delta-grid-two-rows-layout.json \
  /tmp/delta-grid-two-rows-debug.svg
```

Add `--profile` to print post-warmup averages/minimums/maximums for
`UiDocument.Layout`, `BuildDisplayList`, `UiDisplayListGraphFeature.Consume`,
`IRenderGraph.Build` and `IRenderGraph.Execute`. The execute value includes CPU
command recording/submission and the wait for the previous-frame fence; it is a
GPU-completion proxy, not a hardware GPU timestamp. For a bounded measurement:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64 --no-build -- --headless --frames 300 --profile
```
