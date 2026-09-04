# DeltaXAML TODO

## Cross-project integration

Cross-project ownership, integration and shared acceptance remain tracked in
[../CONTRACTS.md](../CONTRACTS.md). Add future project work here only
after it has been explicitly selected; keep research and unselected alternatives
in [IDEAS.md](IDEAS.md).

## Design — independent element and text placement (`DXAML-LAYOUT-TEXT-1`)

**Status: design recorded; implementation is not started by this entry.**

### Audit against the current implementation

The following parts are already implemented and are deliberately not repeated
as TODO items here:

- `Width`/`Height`, automatic `NaN` dimensions, basic `Margin` and existing
  `Fill` behavior are present on the retained element path;
- `HorizontalTextAlignment`/`VerticalTextAlignment`, wrapping, trimming,
  maximum lines, line height, font weight/style and decorations are present for
  text controls and have typed-state/compiler/loader coverage;
- text layout already has an internal `TextBounds` distinct from the element
  `Bounds`, and visual extraction uses it for the neutral text request;
- detached hierarchy JSON and unchanged display-list storage/version reuse are
  already implemented and tested;
- DPI invalidation and non-compounding text scaling are covered by the current
  runtime tests and are outside this design slice.

The remaining work is specifically the missing element-level placement model
and its integration with the already working text-level placement. Existing
features above must remain regression coverage, not be reimplemented.

### Intent

The layout model must distinguish two different operations that are currently
easy to confuse in XAML:

1. placing and sizing an element inside the slot supplied by its parent;
2. placing the element's content, especially text, inside the element's own
   arranged content box.

The distinction applies to every element that can be arranged, not only to
`TextBlock`. A `Border`, `ContentControl`, `Button`, `TextBox`, numeric editor,
image and future controls must receive the same element-level sizing and
placement semantics. Text controls additionally expose text-level alignment.

The goal is deterministic logical layout with no renderer-side correction. A
clip remains a visibility boundary and must not be used to hide a wrong
arrangement.

### Terminology and ownership

```text
parent slot
    -> element margin box
    -> element arranged Bounds
    -> element content box (padding/border inset)
    -> text layout box
    -> shaped glyph origin / lines
```

- The parent layout algorithm owns the slot it offers to a child.
- `UiElement` owns the child's requested size, arranged `Bounds`, margin and
  element-level alignment state.
- A content control owns the content box it offers to its child after its
  padding and border insets.
- `UiTextBlock`/the text part of `UiTextBox` and `UiNumericEditor` owns the
  text layout box, line placement and glyph origin.
- DeltaXAML produces logical bounds and text placement. DeltaText shapes the
  text; DeltaRender consumes the resulting neutral display list. Neither
  downstream project re-arranges XAML elements.

### Proposed user API

The following is the target shape, not an API claim for the current build:

```csharp
public enum UiHorizontalAlignment : byte
{
    Unknown = 0,
    Start = 1,
    Center = 2,
    End = 3,
    Stretch = 4,
}

public enum UiVerticalAlignment : byte
{
    Unknown = 0,
    Start = 1,
    Center = 2,
    End = 3,
    Stretch = 4,
}

public abstract class UiElement
{
    public float Width { get; set; }     // NaN = automatic
    public float Height { get; set; }    // NaN = automatic
    public UiThickness Margin { get; set; }
    public UiHorizontalAlignment HorizontalAlignment { get; set; }
    public UiVerticalAlignment VerticalAlignment { get; set; }
}

public sealed class UiTextBlock : UiElement
{
    public UiTextHorizontalAlignment HorizontalTextAlignment { get; set; }
    public UiTextVerticalAlignment VerticalTextAlignment { get; set; }
}
```

`Start`/`End` are intentional rather than `Left`/`Right`: they leave room for
an explicit future flow-direction policy without changing the meaning of
element placement. The text API currently uses `Left`/`Right`; a separate
direction/bi-directional text decision is required before renaming those
existing values.

The new alignment values must be stored once in the retained typed state and
must flow through generated descriptors, compiled setters and the interpreted
cold loader. They must not be implemented as a second property store or as a
renderer hint. `Fill` is not a second semantic axis: during migration its
source syntax should translate to `Stretch` in the semantic model, after which
the runtime has one alignment representation. Removing the old `Fill` member
requires a separate compatibility/removal decision; this design does not
silently remove it.

### Measure and arrange rules

All controls use the same high-level rules; the control-specific mixin only
defines its intrinsic desired size and content arrangement.

1. The parent computes a child slot in logical coordinates, subtracting the
   child's `Margin` according to the parent layout policy.
2. The child measures against the available slot. An explicit finite
   `Width`/`Height` is a requested size; `NaN` means automatic/intrinsic size.
3. The arranged element size is selected per axis:
   - explicit dimension: use the explicit dimension;
   - automatic + `Stretch`: use the available slot;
   - automatic + `Start`/`Center`/`End`: use the desired size.
4. The element is positioned in the slot by its horizontal and vertical
   alignment. Explicit dimensions never silently turn into stretch dimensions.
5. A content element (for example `Border`) creates an inner content box by
   subtracting padding and the control's content-affecting insets. It arranges
   its child in that box using the child's element-level alignment.
6. A text element measures and positions text independently inside its own
   content box using `HorizontalTextAlignment` and
   `VerticalTextAlignment`. The text alignment does not move the
   `TextBlock`'s outer `Bounds`.

The conceptual calculation for one axis is:

```text
available = max(0, parentSlot - margin)
desired   = Measure(content, available)
size      = explicit ? explicit : alignment == Stretch ? available : desired
origin    = Align(parentSlot, size, alignment, margin)
elementBounds = (origin, size)
```

For a text control:

```text
contentBox = Inset(elementBounds, padding)
textSize   = MeasureText(text, contentBox, wrapping, maxLines, font)
textOrigin = Align(contentBox, textSize, textAlignment)
```

Thus a centered `TextBlock` can be either a centered element inside a `Border`,
text centered inside a full-size `Border` child, or both. These are distinct
and independently useful compositions:

```xml
<Border Width="300"
        Height="150"
        HorizontalAlignment="Center"
        VerticalAlignment="Center">
    <TextBlock Text="Delta"
               HorizontalAlignment="Stretch"
               VerticalAlignment="Stretch"
               HorizontalTextAlignment="Center"
               VerticalTextAlignment="Center" />
</Border>
```

The first pair centers the `Border` in its parent's slot. The second pair makes
the text element occupy the `Border` content box. The text pair centers the
glyph layout within that element. Removing any one pair must produce the
corresponding, observable difference rather than an implicit fallback.

### Remaining invalidation and retained-state work

Invalidation follows the affected stage and never rebuilds the whole document:

| Change | Required invalidation | Reason |
| --- | --- | --- |
| `Width`/`Height` | Measure, arrange, visual | desired size and descendants' slots may change |
| `Margin` | Measure, arrange, visual | parent allocation and element origin change |
| `HorizontalAlignment`/`VerticalAlignment` | Arrange, visual | size/origin may change; intrinsic content is unchanged |
| `Padding` or content-affecting inset | Measure, arrange, visual | content slot and child/text bounds change |
| parent resize | Measure/arrange only for affected subtree, then visual | available slots change |

An unchanged text value keeps its content/style identity and shaped-text cache
identity. A text-only alignment change may change geometry and display-list
payload without pretending that the string changed. A resource or binding
update invalidates only the dependent effective property and its declared
stages.

### Diagnostics and observability

The existing detached layout JSON remains the debug source of truth. The
implementation slice should add, for text controls, a `textBounds`/text-layout
box alongside `bounds`, while preserving the existing schema and making the
field optional for non-text elements. Each node should make it possible to
answer separately:

- what size the parent offered;
- what size the element requested and received;
- where the element was arranged;
- where its text was laid out;
- which clip was inherited.

The diagnostic serializer is cold-path tooling: it may allocate a detached
string and must never be used by measure, arrange, input or visual extraction.

### Parser and compiler mapping

The compiler and cold loader accept the same canonical names for the new
element-level properties and emit the same typed values. Invalid enum values,
negative dimensions, non-finite dimensions and impossible thickness values
produce stable diagnostics or the existing argument exceptions at the same
boundary as the corresponding typed setter.

The semantic artifact contains alignment values, not source strings. Generated
factories and setters write the retained typed state directly. The steady-state
layout path performs no reflection, string property search, boxing or parent
walk to recover alignment.

### Acceptance tests

The implementation is complete only when headless tests cover all of the
following:

- a fixed-size child centered in a larger `Panel`, `Grid` cell and `Border`;
- automatic-size versus `Stretch` behavior for every control that derives from
  `UiElement`;
- independent element placement and text placement in the same fixture;
- `Border` padding/content-box effects without accidental child clipping;
- explicit `Width`/`Height` overriding stretch size while alignment still
  determines origin;
- resize to a smaller viewport with no visible bounds outside the intended
  slot, verified from layout JSON rather than a screenshot heuristic;
- parser and generated-artifact parity for the alignment properties;
- exact invalidation flags for each table row above;

### Phased implementation and open decisions

1. Add the two universal alignment enums and typed property metadata.
2. Implement one shared axis-sizing/placement algorithm used by the existing
   panel, grid, stack and content controls.
3. Migrate `Border`/`ContentControl` to a single inner-content-slot helper.
4. Apply the same element placement to text, image, value and item controls;
   keep text alignment in the text capability only.
5. Align generated and cold-loader syntax, add JSON diagnostics and the tests
   above.
6. Decide the removal milestone for the `Fill` syntax/member after all current
   samples are migrated; do not retain two runtime representations.

Before implementation, the owner must explicitly decide:

- whether the public names are `Start`/`End` or the existing physical
  `Left`/`Right` terminology;
- default element alignment (`Stretch` is the proposed default);
- whether margin is part of every control's public surface or only layout
  containers;
- whether `textBounds` is added to layout diagnostics in the next schema
  version;
- how a future flow direction maps `Start`/`End` and existing text alignment.

No DeltaRender, DeltaText, Vulkan or cross-project contract change is required
for this design. The producer continues to emit logical bounds, clips and
neutral text requests; the consumer receives the final placement as data.
