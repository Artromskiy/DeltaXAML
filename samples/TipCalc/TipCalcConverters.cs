using System.Globalization;
using Delta.XAML;

namespace DeltaXaml.Samples.TipCalc;

/// <summary>Typed converter functions called directly by the generated binding plans.</summary>
public static class TipCalcConverters
{
    [UiXamlConverter("OptionalDouble")]
    public static string ToOptionalText(double value) =>
        value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

    [UiXamlConverter("OptionalDouble", UiXamlConverterDirection.Backward)]
    public static double FromOptionalText(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    [UiXamlConverter("HalfStep")]
    public static double RoundToHalf(double value) => RoundHalf(value);

    [UiXamlConverter("HalfStep", UiXamlConverterDirection.Backward)]
    public static double RoundHalfBack(double value) => RoundHalf(value);

    private static double RoundHalf(double value) => 0.5 * Math.Round(value / 0.5);
}
