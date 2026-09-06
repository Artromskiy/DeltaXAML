# DeltaXAML agent router

Scope: Delta-owned XAML dialect, retained UI tree, properties/bindings,
controls, layout, hit testing, input routing and renderer-neutral primitives.
DeltaXAML is a library: it does not own the application loop, ECS storage,
SDL, Vulkan, DeltaRender, DeltaEngine or Roslyn.

## Map — open only as needed

- ../CODE_STYLE.md — technical data-flow, type, allocation and source-generation rules.
- ../CONTRACTS.md — canonical UI/text/render ownership; open only for a boundary task.
- IDEAS.md — language/designer research/options only when requested.
- WORKFLOW.md — fast build, headless harness and project checks.
- docs/CONTRACT.md — frozen cross-project input/display contract; do not edit for implementation cleanup.
- docs/USER_API.md and docs/LIBRARY_CONTRACT.md — user-facing/library API; open only for public API/documentation work.
- docs/INTERNAL.md and docs/ARCHITECTURE.md — retained pipeline internals.
- src/DeltaXAML and src/DeltaXAML.* — production library, contract and implementation siblings.
- tests, samples, tools — verification, runnable examples and developer tooling.

The external maui-skills material is reference only; do not reproduce MAUI
implementation or add a MAUI dependency. Use compiler-frontend, static-analysis,
performance-speedup and css-coder only for the matching bounded area.
