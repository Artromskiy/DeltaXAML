# UI Library Demo

This is a minimal DeltaXAML panel sample. It renders a generated square grid
behind one centered Tabs panel. The logical cell size follows the current
viewport, so cells stay square in headless and windowed resolutions.

The composition uses regular DeltaXAML controls and nested content; there is no
host-built substitute tree. The executable wires the generated artifact directly
to `VulkanRenderer`, `TextRenderFeature`, `UiDisplayListGraphFeature`, and a
small sample-owned render graph. `DeltaRender.UI` is not required.

Run the sample directly and inspect the hierarchy snapshot:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \
  -c Release -- --headless --frames 1 \
  --layout-json /tmp/delta-ui-library-demo-layout.json
```

The sample uses `DeltaRender.UI` directly. Its grid lines are generated into
the named `ItemsControl` hosts; the XAML file contains no repeated line
declarations.

Windowed mode runs until the window is closed:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \
  -c Release -- --frames 0
```

Headless mode renders a bounded frame and can save a PPM readback:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \
  -c Release -- --headless --frames 1 \
  --width 1280 --height 860 --readback /tmp/delta-ui-library-demo.ppm
```
