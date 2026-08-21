# DeltaXAML

Platform-neutral retained UI and a Delta-owned XAML dialect shared by game and
editor. It has no SDL, Vulkan, Avalonia, DeltaEngine, DeltaRender, Roslyn or ECS
storage dependency.

```text
XAML -> retained tree -> properties/styles -> measure/arrange
  -> hit testing/input -> renderer-neutral primitives and positioned text
```

`DeltaXAML.Abstractions` owns neutral element/frame/draw contracts.
`DeltaXAML.Core` owns parsing, retained identity, invalidation, controls,
bindings, layout, clipping, focus and routed input. Font shaping/rasterization
and GPU atlases remain behind external contracts.

The dialect intentionally does not reproduce WPF/Avalonia's full dependency
property system. Unsupported elements/properties produce source diagnostics.
ECS/editor data enters through neutral schema/value/edit records rather than
storage handles or reflection objects.

See [WORKFLOW.md](WORKFLOW.md) for headless checks, [TODO.md](TODO.md) for
selected work, [IDEAS.md](IDEAS.md) for deferred tooling and
[../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) for shared acceptance.
