# DeltaXAML

DeltaXAML is the retained XAML library owned by DeltaXAML. It provides XAML
loading, retained elements, properties, bindings, layout and neutral input.
It has no SDL, Vulkan, DeltaEngine or ECS-storage dependency.

Authoritative documents:

- [USER_API.md](USER_API.md) — explicitly user-facing library API;
- [LIBRARY_CONTRACT.md](LIBRARY_CONTRACT.md) — selected loader/document/
  element/property contract;
- [CONTRACT.md](CONTRACT.md) — cross-project input and borrowed
  display-list contract;
- [INTERNAL.md](INTERNAL.md) — authoritative typed-state, static-mixin,
  generated-descriptor and compiled-XAML implementation architecture;
- [TODO.md](../TODO.md) — currently selected project work;
- [WORKFLOW.md](../WORKFLOW.md) — bounded local checks.

Ownership is intentionally split: DeltaXAML owns the retained document and
display-list production, DeltaText owns shaping, DeltaRender owns GPU/atlas
submission, and DeltaEngine owns platform event acquisition and scheduling.
The canonical cross-project namespace is `Delta.XAML.Contract`; retained
implementation details are internal.

DeltaXAML is usable directly inside a game. The host dispatches neutral input,
lays out the retained document for its viewport and adds the borrowed
`UiDisplayList` to DeltaRender's Vulkan render graph. DeltaEditor is one
consumer, not the owner of the library or its lifecycle.
