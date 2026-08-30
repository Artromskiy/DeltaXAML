# Game2048

Headless 2048 sample built with the DeltaXAML generated XAML path. The layout
follows the supplied 2048 design reference: score/best cards, a themed 4×4
board, rounded tiles, new-game actions and a status line.

`Game2048.xaml` owns the complete retained composition. `Game2048State` owns
the deterministic game rules and `Game2048View` updates the existing named
controls; the sample does not construct a substitute UI tree in code. Arrow
and WASD physical-key identities are mapped by the host sample to game moves,
while the packet is also sent through `UiDocument.Dispatch`.

The sample is intentionally headless. It validates generated construction,
layout, neutral text requests, clips, mixed display order and a deterministic
merge. It does not own a window or depend on DeltaRender, SDL, Vulkan or
DeltaEngine.

Run from the DeltaXAML repository root:

```bash
SixLaborsLicenseFile=/Users/rum/GitProjects/TheFurnace/Furnace/Licenses/SixLabors.lic \
  dotnet run --project samples/Game2048/DeltaXAML.Samples.Game2048.csproj -c Release
```
