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
