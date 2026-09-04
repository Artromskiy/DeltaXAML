# YAGE UI library sample

This is a DeltaXAML translation of the local Avalonia reference sample:

`/Users/rum/Documents/ChatGPT/Avalonia-sample-ui-yage`

The sample keeps the reference's dark editor design-system composition:
toolbar, tabs, renderer preview, buttons, inputs, selection, inspector,
assets, status, tree/list and typography cards. It uses one generated
`.dxaml` artifact, resource-backed styles and the existing DeltaXAML controls.

Avalonia-only selectors, `Window`, transforms, `UniformGrid` and drawing
brushes are intentionally represented by the closest supported DeltaXAML
composition instead of a host-built substitute tree.

Run the layout-only sample and inspect the detached hierarchy snapshot:

```sh
dotnet run --project samples/YageUiLibrary/DeltaXAML.Samples.YageUiLibrary.csproj \
  -c Release -- --layout-json /tmp/delta-yage-ui-layout.json
```
