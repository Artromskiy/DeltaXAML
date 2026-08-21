# DeltaXAML

Platform-neutral retained UI and a Delta-owned XAML dialect shared by game and
editor. It has no SDL, Vulkan, Avalonia, DeltaEngine, or DeltaRender dependency.

## Current slice

- `DeltaXAML.Abstractions`: element, frame and renderer-neutral draw contracts.
- `DeltaXAML.Core`: retained tree, `Panel`, measure/arrange, clipping, draw-list
  extraction and XAML loading.
- Loader subset: nested `Panel`/`StackPanel`, orientation, requested
  `Width`/`Height`, `Fill`, clipping and `Background`, with source diagnostics
  for unsupported elements or properties.

```text
XAML
  -> retained tree
  -> style/layout
  -> renderer-neutral draw list
  -> DeltaEngine adapter
  -> DeltaRender UI batches
```

DeltaXAML owns the tree and layout state; DeltaRender owns GPU resources and
consumes extracted data. ECS state does not leak directly into UI internals.

The active milestone is a live component inspector. DeltaXAML owns a compact
property/binding model, retained controls, invalidation, hit testing, focus and
routed events. It produces renderer-neutral UI primitives and positioned text
runs; font shaping, glyph rasterization and GPU atlases stay behind external
interfaces. Do not add the full WPF/Avalonia property system.

Required first controls are `TextBlock`, `Border`, fixed/auto/star `Grid`,
content/button/toggle controls, `TextBox`, a numeric editor and `ScrollViewer`.
The authoritative ownership and acceptance gates are in
[`../EDITOR_UI_TODO.md`](../EDITOR_UI_TODO.md), P4-P8.

## Build and test

```bash
dotnet build DeltaXAML.slnx -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project tests/DeltaXAML.Core.Tests/DeltaXAML.Core.Tests.csproj \
  -c Release
```

The colored editor-shell integration gate is complete. The next gate is a real
`ComponentInspector.xaml` whose fields update from and write back to a selected
DeltaECS entity through an editor-owned neutral adapter.
