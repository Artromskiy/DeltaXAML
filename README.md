# DeltaXAML

DeltaXAML is a retained XAML UI library for games, tools and editors. It turns
XAML or public C# element definitions into deterministic layout, input and
renderer-neutral display data.

## What it provides

- XAML loading with typed elements, properties, resources and diagnostics.
- Retained layout with panels, grids, borders, scrolling and content controls.
- One-time, one-way and two-way bindings with converters and validation.
- Text display and editing through a host-provided text service.
- Neutral input routing, hit testing, focus, capture and IME packets.
- Logical layout diagnostics as deterministic JSON for headless tools.
- A borrowed display list usable by both game UI and editor hosts.

## Quick start

Add the library package to a `net8.0` application:

```xml
<PackageReference Include="DeltaXAML" Version="0.0.14" />
```

Create a document with the public library API, lay it out, then consume the
resulting display list:

```csharp
using Delta.Maths;
using Delta.XAML;
using Delta.XAML.Contract;

var root = new UiPanel();
root.Add(new TextBlock { Text = "Hello, DeltaXAML", FontSize = 18 });
using var document = new UiDocument(root, textService);

document.Layout(new float2(800, 450), 1.0f);
UiDisplayList frame = document.BuildDisplayList();
Console.WriteLine($"visuals={frame.Visuals.Length}, text={frame.Text.Length}");
```

`textService` is supplied by the application. DeltaXAML places text and emits
neutral text requests; the application chooses the shaping and rendering
services.

## Core concepts

The document is retained between frames. Hosts provide input and viewport
size; DeltaXAML updates the document and returns a borrowed frame view:

```text
XAML/C# -> UiDocument -> Dispatch + Layout -> UiDisplayList -> host renderer
```

Call `BuildLayoutDiagnosticsJson()` after layout when comparing authored XAML
dimensions with final bounds, clips and child hierarchy.

## Capabilities and limits

The library targets `net8.0` and is platform-neutral. It does not create a
window, own an application loop or submit GPU work. It has no SDL, Vulkan,
engine or ECS dependency. Layout coordinates are logical, top-left UI values;
the rendering host applies its device-pixel and backend policy.

DeltaXAML does not rasterize fonts, decode images, choose GPU pipelines or
provide a platform accessibility backend. Those services remain host-owned.
Unsupported XAML constructs return diagnostics rather than silently creating a
different tree.

## Packages and examples

- [`DeltaXAML`](https://www.nuget.org/packages/DeltaXAML) — retained UI library.
- [`DeltaXAML.Contract`](https://www.nuget.org/packages/DeltaXAML.Contract) —
  neutral input and display-list boundary for consumers.
- [`DeltaXAML.Compiler`](https://www.nuget.org/packages/DeltaXAML.Compiler) —
  build-time XAML semantic compiler.
- [`DeltaXAML.Generator`](https://www.nuget.org/packages/DeltaXAML.Generator) —
  source-generator package for compiled XAML.
- [`RoundedRectangle.Headless`](samples/RoundedRectangle.Headless/README.md) —
  headless retained layout and display-list example.
- [`RoundedRectangle.Render`](samples/RoundedRectangle.Render/README.md) —
  DeltaRender-backed rounded-rectangle example.
- [`Snake`](samples/2048-electron/README.md) — interactive XAML game sample.

## Further reading

- [User API](docs/USER_API.md)
- [Library contract](docs/LIBRARY_CONTRACT.md)
- [Cross-project contract](docs/CONTRACT.md)
