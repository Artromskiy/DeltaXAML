# DeltaXAML agent guide

Scope: Delta-owned XAML dialect, retained UI tree, properties/bindings,
controls, layout, hit testing, input routing and renderer-neutral primitives.

- [README.md](README.md) — stable UI ownership and pipeline.
- [TODO.md](TODO.md) — selected UI work.
- [IDEAS.md](IDEAS.md) — deferred language/designer features.
- [WORKFLOW.md](WORKFLOW.md) — fast build, headless harness and checks.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) — shared inspector acceptance.

DeltaXAML must not depend on SDL, Vulkan, DeltaRender, DeltaEngine, Roslyn or
ECS storage. Font rasterization and shaping remain external.

Skills: `compiler-frontend` for loader/parser/diagnostics,
`static-analysis` for dependency-boundary review, `performance-speedup` for
retained invalidation/allocation work, and `css-coder` only as design-system
inspiration without importing browser semantics.
