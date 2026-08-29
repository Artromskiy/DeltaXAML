# DeltaXAML ideas

Not active work:

- Schema-driven VS Code completion, diagnostics, preview and hot reload.
- Theme resources/templates after primitive controls and invalidation stabilize.
- Accessibility and full IME composition after neutral input is proven.
- A Grid/design dialect compatible in spirit with XAML tooling; native Blend
compatibility is not assumed.
- Compiler-only text-content sugar for single-content controls, for example
  `<Button>Save</Button>` (and, if selected, `<Button Content="Save" />`). The
  typed artifact would lower the string to a generated `UiTextBlock` child with
  stable identity and source mapping. This must not add `UiButton.Text`, an
  object-valued runtime content slot, implicit runtime conversion or a second
  text path; whitespace, localization, binding and inherited-style semantics
  must be specified before selection.
