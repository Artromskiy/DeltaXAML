# UI Library Demo

A DeltaXAML visual port of the user-provided YAGE Electron UI gallery:
16 sections, dark cards over a square grid, shared styles, typed item templates,
Inter UI typography and JetBrains Mono data text.

- [MainWindow.dxaml](MainWindow.dxaml) — theme, templates and composition.
- [DemoModel.cs](DemoModel.cs) — typed data for repeated elements.
- [UiLibraryDemoContent.cs](UiLibraryDemoContent.cs) — responsive card slots and document owner.
- [VISUAL_GAPS.md](VISUAL_GAPS.md) — source attribution, unsupported options and explicit approximations.

The existing renderer is wired directly in Program; no UiRenderHost or
host-built substitute tree. First-party dependencies stay floating NuGet
references, resolved from the workspace dev feed during local development.
All fonts needed by this sample are included; Electron is not a runtime dependency.

From the DeltaXAML checkout, after the workspace dev dependency workflow:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \\
  -c Release -r osx-arm64 -- --frames 0
```

Headless Vulkan readback and the actual document layout (no window):

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \\
  -c Release -r osx-arm64 -- --headless --frames 2 \\
  --width 1280 --height 1200 --dpi 2 \\
  --readback /tmp/ui-library-demo.ppm --layout-json /tmp/ui-library-demo.json
sips -s format png /tmp/ui-library-demo.ppm --out /tmp/ui-library-demo.png
```

The long page scrolls in windowed mode. Cards reflow according to the viewport;
the background grid retains a 32×32 logical-unit pitch. Headless defaults to one
frame; windowed mode has no frame limit. This is visual parity work, not a
complete editor: see the explicit limits in VISUAL_GAPS.md.

For a bounded render-side scroll repro, send one synthetic wheel event before
the second frame:

```sh
dotnet run --project samples/UI-Library-Demo/DeltaXAML.Samples.UI.Library.Demo.csproj \\
  -c Release -r osx-arm64 -- --frames 2 --synthetic-scroll
```
