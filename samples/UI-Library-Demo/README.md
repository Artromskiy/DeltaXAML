# UI Library Demo

This is a DeltaXAML editor-style UI library sample. It demonstrates a dark
design-system composition with:
toolbar, tabs, renderer preview, buttons, inputs, selection, inspector,
assets, status, tree/list and typography cards. It uses one generated
`.dxaml` artifact, resource-backed styles and the existing DeltaXAML controls.

Avalonia-only selectors, `Window`, transforms, `UniformGrid` and drawing
brushes are intentionally represented by the closest supported DeltaXAML
composition instead of a host-built substitute tree.

Run the sample through the shared host and inspect the hierarchy snapshot:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \
  -c Release -- --headless --frames 1 \
  --layout-json /tmp/delta-ui-library-demo-layout.json
```

The demo uses `DeltaRender.UI` directly. Its grid background is generated into
the named `ItemsControl` hosts during content setup; the XAML file contains no
repeated line declarations.

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
