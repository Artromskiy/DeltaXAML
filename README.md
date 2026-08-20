# DeltaXAML

Platform-neutral retained UI and a Delta-owned XAML dialect shared by game and
editor. It has no SDL, Vulkan, Avalonia, DeltaEngine, or DeltaRender dependency.

## Current slice

- `DeltaXAML.Abstractions`: element, frame and renderer-neutral draw contracts.
- `DeltaXAML.Core`: retained tree, `Panel`, measure/arrange, clipping, draw-list
  extraction and XAML loading.
- Loader subset: `Panel`, `Width`, `Height`, and `Background` with source
  diagnostics for unsupported elements or properties.

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

Bindings, text shaping, templates, keyboard focus, routed events, atlases and
advanced layout are later vertical slices. Do not add the full WPF/Avalonia
property system.

## Build and test

```bash
dotnet build DeltaXAML.slnx -c Release \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project tests/DeltaXAML.Core.Tests/DeltaXAML.Core.Tests.csproj \
  -c Release
```

The next integration gate is a parsed panel producing a draw list that the
DeltaEngine host submits through DeltaRender and presents on MoltenVK.
