# DeltaXAML internal implementation

This file is explicitly internal. It documents implementation mechanics and
must not be treated as a consumer or cross-project contract.

## Current retained implementation

`DeltaXAML.Core` currently contains the retained `UiElement` tree, controls,
`UiPropertyStore`, bindings, resource/style stores, measure/arrange,
hit-testing, input routing and reusable draw-list storage. These types still
use the temporary compatibility assembly while the public facade migration is
in progress.

The retained dirty accumulator, generations, local/style/binding/handle
source slots, recursive mutation lookup and reusable arrays are internal
policy. They must not be copied into a second public tree or property engine.
The legacy draw storage is borrowed until the next extraction and is not a
long-lived renderer resource.

## Adapter boundary

`src/Delta.XAML.Contract` is the only cross-project packet producer. It owns:

- `UiInputEvent` and its pointer/key/text/composition payloads;
- `UiDisplayList`, `UiVisualCommand`, `UiClip` and `UiTextDraw`;
- typed `Guid` resource and semantic visual identities.

DeltaXAML may adapt retained nodes into those packets, but must not put SDL,
Vulkan, ECS, engine lifecycle, atlas pages, UVs, pipeline IDs or GPU handles
into the contract. `UiClipId` and generations are frame/runtime-local integer
values, not durable resource identities.

## Migration notes

The canonical library facade is `UiDocument` plus `IXamlLoader` from
`LIBRARY_CONTRACT.md`. The existing implementation is retained only to keep
the current headless harness working during migration. A facade adapter may
delegate to that tree, but it must preserve one retained identity and one
property/binding store. It must not rebuild a parallel tree each frame.

The loader adapter now passes unknown element names through the canonical type
resolver and translates GUID-valued `ForegroundResource` references through
the canonical resource resolver into the compatibility store for the load.
That translation is load-scoped; the compatibility store does not provide a
canonical resource-change subscription.

The document adapter maps only legacy rectangle commands to canonical solid
rectangles. If it sees retained text, a non-rectangle visual kind, a non-empty
legacy resource handle or a visual text payload, `TryBuildDisplayList` returns
a typed diagnostic and `BuildDisplayList` throws; it never silently drops the
data or relabels it as a solid rectangle.

The canonical display-list text value is already shaped `ShapedText`; font
instance resolution and shaping are owned by DeltaText. Existing retained text
request values are not a substitute for the canonical `UiTextDraw` value.
Until a font-instance mapping is supplied by the library composition boundary,
the legacy-to-canonical text adapter remains an explicit migration blocker.

Loader diagnostics now cross the library boundary as
`Delta.Diagnostics.Diagnostic`; the adapter maps the compatibility loader's
source positions to zero-based ranges. The diagnostics producer is referenced
as a normal multi-target project and no local diagnostic type is maintained.
The compatibility parser still does not consume canonical type/resource
resolvers: it has no lossless mapping from GUID resource identities to its
string-keyed store, and the existing type registry has a different factory
shape. Those are explicit migration gaps rather than alternate public APIs.

## Non-goals

There is no internal promise of full XAML standard compatibility, reflection
discovery, WPF/Avalonia dependency-property semantics, ECS bindings, native
window ownership or GPU submission.
