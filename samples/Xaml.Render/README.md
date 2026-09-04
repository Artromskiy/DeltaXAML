# DeltaXAML XAML Render sample

This is the in-repository renderer host for compiled `.dxaml` documents. The
host itself is not a NuGet package and is not a runtime dependency of other
samples. Public DeltaXAML, DeltaRender and DeltaShader contracts are consumed
through `PackageReference`; local development resolves them with the
workspace NuGet feed described in [`docs/NUGET_WORKFLOW.md`](../../../docs/NUGET_WORKFLOW.md).
The generator is a build-time local project reference because the current
published generator package does not load its companion analyzer assemblies
with this SDK; this does not add a runtime dependency or a second XAML path.
The two current renderer integration adapters are still project references
because their producer projects are explicitly non-packable.

The host selects one generated artifact with `--sample`: `main-window` (the
runner fixture), `yage`, `rounded`, or `grid-two-rows`. Each selection creates
one `UiDocument` and sends its borrowed `UiDisplayList` to
`UiDisplayListGraphFeature`. Shader SPIR-V, ABI and typed packing come from the
shared DeltaShader UI/Text producer projects. DeltaXAML itself remains
independent of SDL, Vulkan and DeltaRender.

From the DeltaXAML repository root:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --frames 1
```

Run an existing static XAML fixture through the same host:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --sample rounded --frames 1
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --sample yage --frames 1
```

For an offscreen check and a PPM readback:

```bash
dotnet run --project samples/Xaml.Render/DeltaXAML.Samples.Xaml.Render.csproj \
  -c Release -r osx-arm64 -- --headless --frames 2 \
  --readback /tmp/delta-xaml-render.ppm
```

The default mode is windowed and runs until the window is closed. The headless
mode is bounded by `--frames`; it does not create an SDL window. The interactive
`Snake` sample keeps its own game/input loop and is not folded into this static
document selector.
