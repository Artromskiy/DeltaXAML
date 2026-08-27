namespace DeltaXAML.Internal;

internal static class XamlBindingParser
{
    internal static bool TryParse(string value, out UiBindingSpec spec, out string? error)
    {
        spec = default;
        error = null;
        if (!value.StartsWith("{Binding", StringComparison.Ordinal))
        {
            return false;
        }

        if (!value.EndsWith('}'))
        {
            error = "Binding markup must end with '}'.";
            return false;
        }

        var body = value[8..^1].Trim();
        var path = string.Empty;
        UiBindingMode mode = UiBindingMode.OneWay;
        string? converter = null;
        string? format = null;
        var parts = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                if (i != 0 || path.Length != 0)
                {
                    error = $"Unexpected binding argument '{part}'.";
                    return false;
                }

                path = part;
                continue;
            }

            var key = part[..separator].Trim();
            var argument = Unquote(part[(separator + 1)..].Trim());
            switch (key)
            {
                case "Path":
                    path = argument;
                    break;
                case "Mode" when Enum.TryParse(argument, true, out UiBindingMode parsedMode):
                    mode = parsedMode;
                    break;
                case "Converter":
                    converter = argument;
                    break;
                case "StringFormat":
                    format = argument;
                    break;
                default:
                    error = $"Unsupported binding argument '{key}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Binding Path is required.";
            return false;
        }

        spec = new UiBindingSpec(string.Empty, path, mode, converter, format);
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
        {
            return value[1..^1];
        }

        return value;
    }
}
