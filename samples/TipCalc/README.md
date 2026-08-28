# TipCalc

Runnable headless DeltaXAML adaptation of the MIT-licensed
[`dotnet/maui-samples` TipCalc](https://github.com/dotnet/maui-samples/tree/e78b47511ac706bb179abc4a09f182355ae75178/10.0/Apps/TipCalc).
The source snapshot is pinned to commit
`e78b47511ac706bb179abc4a09f182355ae75178`.

The sample keeps the original behavior: enter the food-and-drink subtotal and
post-tax total, choose a tip percentage, calculate the tip from the subtotal,
then round the final total to the nearest quarter. The generated XAML artifact
uses typed two-way bindings and converters; it is laid out through `UiDocument`
and produces the canonical borrowed display list with neutral text draws.

`ContentPage`, platform-specific margins and native widgets are intentionally
host concerns. Their retained equivalents here are `Grid`, `TextBox`,
`TextBlock`, `Border` and `Slider`.

Run from the DeltaXAML repository root:

```bash
dotnet run --project samples/TipCalc/DeltaXAML.Samples.TipCalc.csproj -c Release
```
