# Rounded rectangle renderer sample

This sample loads [`RoundedRectangle.xaml`](RoundedRectangle.xaml) through the
DeltaXAML `XamlLoader`, builds one retained `UiDocument`, and submits its
canonical `UiDisplayList` through `DeltaRender.XAML.UiDisplayListGraphFeature`.
The rounded rectangle uses the generated `DeltaShader.UI` rounded-rectangle
artifacts and four independent corner radii.

Run continuously until the window is closed from the DeltaXAML repository root:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64
```

Use `--frames N` for a bounded run. The host requires an SDL3
display, Vulkan/MoltenVK and compatible checked-out DeltaRender/DeltaShader
artifacts. DeltaXAML itself remains renderer-neutral.

Add `--profile` to print post-warmup averages/minimums/maximums for
`UiDocument.Layout`, `BuildDisplayList`, `UiDisplayListGraphFeature.Consume`,
`IRenderGraph.Build` and `IRenderGraph.Execute`. The execute value includes CPU
command recording/submission and the wait for the previous-frame fence; it is a
GPU-completion proxy, not a hardware GPU timestamp. For a bounded measurement:

```bash
dotnet run --project samples/RoundedRectangle.Render/DeltaXAML.Samples.RoundedRectangle.Render.csproj \
  -c Release -r osx-arm64 --no-build -- --headless --frames 300 --profile
```
