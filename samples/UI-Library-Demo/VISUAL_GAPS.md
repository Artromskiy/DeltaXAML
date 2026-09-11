# YAGE visual port — differences and missing capabilities

Source: the user-provided `/Users/rum/GitProjects/yage-ui-library-sample`,
specifically `index.html`, `styles.css`, `fonts.css` and `fonts/`.
The Electron project was not modified. This is a **visual gallery**, not an
implementation of an editor, asset browser or CSS engine.

## What is reused

All 16 source sections are represented by the ordinary generated DXAML path.
Resources and eight named styles share the palette, typography, cards, buttons,
badges and fields. Typed `ItemsSource` + templates construct tabs, action/menu
buttons, icons, badges, statuses, swatches, markers and both grids. Grid item
keys remain stable on resize. The background pitch is 32 logical units; the
preview uses 16. No sample-specific shader or contract extension is required.

## Required for closer visual parity

| Capability | Existing API / limitation | Current sample treatment |
| --- | --- | --- |
| Rotation / render transforms | There is no built-in public `RenderTransform`/`Rotate` property preserving normal layout, clips and hit testing. | Preview entity is a centered 22×22 rounded square without the source's 18° rotation and 52% horizontal offset. |
| Dashed/per-side stroke and remaining inner-effect coverage | Public `BorderWidth` is still a scalar solid stroke and has no dash pattern. DeltaXAML authors `InnerShadow`/`InnerGlow` through the shared typed `EffectSet`; the renderer currently exposes the prepared analytic visual `InnerShadow` path, while `InnerGlow` and text inner effects are not available to this sample. | Empty-state boxes use solid strokes; the subtle card inner highlight remains omitted until the required effect path is available. |
| Independent border sides | Current `BorderWidth` does not accept four independent sides. | Toolbar has a full thin outline instead of just top/bottom strokes. Header separator is an ordinary 1-unit Border. |
| CSS auto-fit / minmax / flex-wrap / gap | Grid, star/Auto tracks, attached rows/columns and item grids exist; CSS automatic wrapping and breakpoints are not source constructs. | One resize policy selects card slots and full-width sections through public attached properties. Buttons use a two-column item grid rather than width-dependent flex wrapping; tabs use equal slots. No manual measure/arrange algorithm. |
| Implicit selectors / header-content slots | The generated dialect uses explicit `StyleKey` plus optional semantic `Variant`; `BasedOn` inheritance is supported, while no general ContentPresenter/HeaderedContentControl slot is used for the card header/body. | Eight shared styles and typed repeated-item templates; distinct card contents remain declarative. This is why each card still has one Border, heading and content container. |
| Ready-made themed slider, checkbox, switch, dropdown, tree and expander | `UiSlider`, `UiPicker`, `UiToggleButton`, templates and input semantics exist, but do not supply Chromium's native chrome or CSS component parts. In particular the slider descriptor has no built-in visual capability. | Checkbox/switch silhouettes use ToggleButton, dropdown is a Button, slider is a static 75% track/thumb composition, tree and inspector are initially expanded visual examples. These previews do **not** claim working dropdown selection, expansion or slider editing. |
| Letter spacing and browser font fallback | No public tracking/letter-spacing control or automatic per-glyph browser fallback stack is configured. | Exact font files, explicit font keys and Material Symbols for icons; no .03em/.1em tracking. Native symbol silhouettes differ from browser fallback glyphs. |

## Issues actually encountered during the port

- Variable WOFF2 + `wght=500` threw `EndOfStreamException` in
  `SixLabors.Fonts.Tables.AdvancedTypographic.Variations.GlyphVariationData.DecodePackedDeltas`
  via `DeltaTextService.Shape`. Original WOFF2 files are retained for reproduction;
  the sample loads static TTF instances generated from the same bytes with FontTools.
  This does not extend or patch DeltaText.
- ScrollViewer's unconstrained horizontal measurement does not make the page's
  star grid automatically occupy viewport width. An ordinary compiled binding
  sets `PageWidth` from the viewport minus the declared page margins. Layout
  itself remains owned by DeltaXAML.
- Some font glyphs still fail to appear in the Vulkan result (notably the
  Material Symbols `play_arrow` U+E037 and `layers` U+E53B in the Icons card).
  Both codepoints and outline contours exist in the shipped subset. This is
  not claimed as supported pixel parity and needs a separate text/render trace.

## Intentional scope, not missing DeltaXAML API

The original `index.html` contains no application event handlers and its Icons
section contains an unexecuted JavaScript template literal. Here that broken
literal is replaced with the intended twelve generated icon buttons.

The sample keeps the direct renderer setup (no UiRenderHost). Window close,
pointer and wheel events are forwarded; the outer page scrolls through the
document. Menu actions, search filtering, editor commands, full text/IME input
forwarding and persistent selections are not part of this visual-copy task.
Do not infer that their omission means the public DeltaXAML input/binding APIs
are absent. The small-screen CSS rearrangements below 700px are also not a
pixel-parity claim; the source desktop window itself has a 720px minimum width.

## Font provenance

`Inter.woff2`, `JetBrainsMono.woff2`, `MaterialSymbols.woff2` were copied from the
source project's `fonts/` directory. Inter and JetBrains Mono use SIL OFL 1.1;
Material Symbols uses Apache 2.0. Notices are included in `Assets/*-OFL.txt` and
`Assets/MaterialSymbols-LICENSE.txt` and copied with the executable.

FontTools 4.64.0 conversion: WOFF2 decompression; static Inter `wght=500`/`600`
and JetBrains Mono `wght=500`; Material Symbols `wght=400` subset to the used
PUA codepoints. These are asset-processing steps, **not runtime dependencies**.
Original WOFF2 files are source inputs only and are not copied to output.
