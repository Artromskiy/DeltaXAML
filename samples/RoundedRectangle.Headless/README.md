# RoundedRectangle.Headless

This sample loads `RoundedRectangles.xaml` through the ordinary DeltaXAML
loader, lays out a retained hierarchy of 100 rounded rectangles and extracts
one borrowed canonical display list. It has no window, GPU, DeltaRender or
DeltaEngine dependency.

After a warm-up it measures the retained frame path for:

- unchanged `Layout` plus `BuildDisplayList`;
- rotating one child in the hierarchy;
- changing one rectangle's size;
- changing one rectangle's paint/radius;
- alternating the viewport DPI scale.

Each result reports average microseconds and managed bytes allocated per frame.
Warm-up, XAML parsing and retained-tree construction are excluded. The
unchanged case is an acceptance check and must remain allocation-free after
warm-up. These are repeatable directional measurements, not a BenchmarkDotNet
statistical claim.

Run from the DeltaXAML repository root:

```bash
dotnet run --project samples/RoundedRectangle.Headless/DeltaXAML.Samples.RoundedRectangle.Headless.csproj \
  -c Release --no-restore
```

Use a shorter or longer bounded run with `--iterations N`.
