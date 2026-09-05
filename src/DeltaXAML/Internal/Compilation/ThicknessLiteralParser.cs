using System.Globalization;

namespace DeltaXAML.Compiler;

internal static class ThicknessLiteralParser
{
    internal static bool TryParse(string value, out Delta.XAML.UiThickness result)
    {
        ArgumentNullException.ThrowIfNull(value);
        result = default;

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (1 or 2 or 4))
        {
            return false;
        }

        var values = new float[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var component) || !float.IsFinite(component))
            {
                return false;
            }

            values[i] = component;
        }

        if (parts.Length == 1)
        {
            values[1] = values[0];
            values[2] = values[0];
            values[3] = values[0];
        }
        else if (parts.Length == 2)
        {
            values[2] = values[0];
            values[3] = values[1];
        }

        result = new Delta.XAML.UiThickness(values[0], values[1], values[2], values[3]);
        return true;
    }
}
