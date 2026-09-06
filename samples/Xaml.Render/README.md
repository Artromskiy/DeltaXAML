# DeltaXAML XAML Render sample

This is a sample consumer for compiled `.dxaml` documents. It consumes the
single `DeltaRender.UI` NuGet bundle; the bundle owns the renderer-side
DeltaXAML adapter, generated UI/text shader assemblies and feature factories.
Local development resolves it with the workspace NuGet feed described in
[`docs/NUGET_WORKFLOW.md`](../../../docs/NUGET_WORKFLOW.md). The sample owns
only its XAML fixtures, document lifetime, window/headless loop and assets.

The host selects one `.dxaml` source with `--sample`: `main-window`,
`ui-library-demo`, `grid-only` or `rounded`. Each selection creates one
`UiDocument` and sends its borrowed `UiDisplayList` to
`UiDisplayListGraphFeature`. Shader SPIR-V, ABI and typed packing are supplied
by the `DeltaRender.UI` bundle. DeltaXAML itself remains independent of SDL,
Vulkan and DeltaRender.

From the DeltaXAML repository root:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --frames 1
```

Run an existing static XAML fixture through the same host:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --sample rounded --frames 1
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --sample ui-library-demo --frames 1
```

For an offscreen check and a PPM readback:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --frames 2 \
  --readback /tmp/delta-xaml-render.ppm
```

The default mode is windowed and runs until the window is closed. The headless
mode is bounded by `--frames`; it does not create an SDL window. Dynamic
samples such as `Snake` use the same `UiRenderHost` through its content hook,
while keeping their game model and input policy local to the sample.
