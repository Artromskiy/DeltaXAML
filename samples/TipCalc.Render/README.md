# TipCalc renderer sample

This sample is the renderer-backed launch path for the generated `TipCalc.xaml`
composition in an SDL3/Vulkan window. It is deliberately separate from
`DeltaXAML`: the sample owns the references to DeltaRender's Vulkan/SDL3
integration, native packages and shader artifacts. It has no dependency on
DeltaEngine, while the DeltaXAML library remains renderer-neutral.

The XAML source and model are shared with the headless sample at
[`../TipCalc`](../TipCalc). The sample-local adapter lays out the retained
`UiDocument`, copies only the visual commands needed by the sample's reusable
DeltaRender pass, and keeps the borrowed `UiDisplayList` lifetime within the
frame.

Run an interactive window from the repository root:

```bash
dotnet run --project samples/TipCalc.Render/DeltaXAML.Samples.TipCalc.Render.csproj \
  -c Release -r osx-arm64
```

For a bounded one-frame native smoke (the host still needs a display and Vulkan
driver):

```bash
dotnet run --project samples/TipCalc.Render/DeltaXAML.Samples.TipCalc.Render.csproj \
  -c Release -r osx-arm64 -- --frames 1
```

The sample adapter draws the canonical rectangle/clip commands
through DeltaRender's UI panel pass and reports the neutral text requests. The
existing panel shader does not yet consume `UiDisplayList.Text`; text shaping
and glyph rendering remain separate DeltaText/renderer work, not a dependency
or implementation detail of DeltaXAML. Native startup requires the checked-out
DeltaRender source and generated Vulkan artifacts to be from compatible commits.
