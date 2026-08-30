# Game2048 renderer sample

This sample has a windowed SDL3/Vulkan host and a headless offscreen Vulkan mode for
the generated `Game2048.xaml`. It keeps the game state and XAML composition from [`../Game2048`](../Game2048)
and adds only the consumer-side renderer references: DeltaRender, DeltaRender's
SDL3/Vulkan integration, DeltaRender.Text, DeltaShader UI/text artifacts and
the native MoltenVK package. `DeltaXAML` itself remains renderer-neutral.

The host uses the normal path:

```text
SDL3 events -> neutral UiKeyEvent + game state -> UiDocument.Layout
-> UiDocument.BuildDisplayList -> UiDisplayListGraphFeature
-> TextRenderFeature / rounded-rectangle shader -> Vulkan swapchain
```

Headless mode follows the same path, but targets an offscreen RGBA8 image and
reads it back after graph execution. It does not create an SDL window:

```bash
dotnet run --project samples/Game2048.Render/DeltaXAML.Samples.Game2048.Render.csproj \
  -c Release -r osx-arm64 -- --headless
```

The command reports the center pixel and the number of non-zero pixels. A
non-transparent center pixel is a bounded visual-output check; it is not a
pixel-perfect image comparison. `--headless --probe visuals|text|combined` is
also available for isolating payload paths.

Headless mode writes the RGBA readback as `/tmp/delta-2048-current.ppm` and
the retained layout snapshot as `/tmp/delta-2048-layout.json`. Override the
JSON path with `--layout-json <path>`.

Keyboard arrows and WASD move the board. `--frames 1` performs a bounded native
startup check and closes after one submitted frame; without it the window runs
until a close/quit event. `--probe visuals`, `--probe text` and
`--probe combined` isolate the three display-list payload paths for native
diagnostics; `combined` is the default. Resizing reuses the same document and recreates only
the renderer feature that owns viewport-sized resources.

Build and run on macOS:

```bash
dotnet restore samples/Game2048.Render/DeltaXAML.Samples.Game2048.Render.csproj \
  --source /Users/rum/GitProjects/TheFurnace/Furnace/Packages/SixLabors.Fonts-Fork \
  --ignore-failed-sources
dotnet run --project samples/Game2048.Render/DeltaXAML.Samples.Game2048.Render.csproj \
  -c Release -r osx-arm64 -- --frames 1
```

The SDF text SPIR-V files under `shaders/` are sample-owned consumer artifacts
matching `DeltaShader.Text`'s generated `SdfTextGraphicsShaderProgram`; they
contain no XAML or engine state. No DeltaEngine, ECS or native dependency is
referenced by the DeltaXAML library.
