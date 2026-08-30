# DeltaXAML layout diagnostics

These are cold debugging tools. They do not add a renderer dependency and do
not participate in the retained frame path.

## 1. Produce the layout JSON

After a document has been laid out, ask the document for its detached snapshot:

```csharp
document.Layout(viewport, dpiScale);
File.WriteAllText(
    "layout.json",
    document.BuildLayoutDiagnosticsJson(indented: true));
```

The JSON contains the retained hierarchy, child order, requested dimensions,
actual bounds, clips, desired sizes, margins and padding.

## 2. Render translucent boxes outside DeltaXAML

Convert the JSON to an SVG that can be opened in a browser or previewer:

```bash
python3 tools/layout-json-to-svg.py layout.json layout.svg
```

Solid translucent boxes represent `bounds`; red dashed boxes represent
`clip`. The SVG uses the logical viewport coordinates from the JSON and does
not use DeltaRender, Vulkan or SDL.
