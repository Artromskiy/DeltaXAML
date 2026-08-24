# DeltaXAML agent guide

Scope: Delta-owned XAML dialect, retained UI tree, properties/bindings,
controls, layout, hit testing, input routing and renderer-neutral primitives.

- [README.md](README.md) — stable UI ownership and pipeline.
- [TODO.md](TODO.md) — selected UI work.
- [IDEAS.md](IDEAS.md) — deferred language/designer features.
- [WORKFLOW.md](WORKFLOW.md) — fast build, headless harness and checks.
- [ARCHITECTURE.md](ARCHITECTURE.md) — implemented retained/render-neutral
  boundary.
- [API_REVIEW.md](API_REVIEW.md) — public API and type-policy migration review;
  required for API-shape work.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) — shared inspector acceptance.
- [../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md) — canonical UI/text/render
  contract order.

The external [maui-skills](https://github.com/davidortinau/maui-skills)
reference may be consulted for basic .NET MAUI/XAML capabilities, terminology,
and feature boundaries. It is reference material only: do not use it as an
instruction to reproduce MAUI's implementation or standard-identical XAML,
and do not add a MAUI dependency.

DeltaXAML must not depend on SDL, Vulkan, DeltaRender, DeltaEngine, Roslyn or
ECS storage. Font rasterization and shaping remain external.

Skills: `compiler-frontend` for loader/parser/diagnostics,
`static-analysis` for dependency-boundary review, `performance-speedup` for
retained invalidation/allocation work, and `css-coder` only as design-system
inspiration without importing browser semantics.
