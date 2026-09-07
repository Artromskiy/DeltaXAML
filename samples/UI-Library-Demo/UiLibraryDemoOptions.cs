using System.Globalization;
using Delta;
using Delta.Render;
using Delta.Render.RenderGraph;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed record SampleOptions(
    bool Headless,
    bool SyntheticScroll,
    uint Width,
    uint Height,
    float Dpi,
    int Frames,
    string? ReadbackPath,
    string? LayoutPath)
{
    internal const uint DefaultWidth = 1280;
    internal const uint DefaultHeight = 860;

    internal PixelExtent ToPixelExtent() => new(Scale(Width, Dpi), Scale(Height, Dpi));

    internal static SampleOptions Parse(string[] args)
    {
        var headless = HasFlag(args, "--headless");
        var syntheticScroll = HasFlag(args, "--synthetic-scroll");
        var width = ParseUInt(args, "--width", DefaultWidth);
        var height = ParseUInt(args, "--height", DefaultHeight);
        var dpi = ParseFloat(args, "--dpi", 1f);
        var frames = ParseInt(args, "--frames", headless ? 1 : 0);
        ArgumentOutOfRangeException.ThrowIfNegative(frames);

        return new(
            headless,
            syntheticScroll,
            width,
            height,
            dpi,
            frames,
            GetOption(args, "--readback"),
            GetOption(args, "--layout-json"));
    }

    private static uint Scale(uint logical, float dpi) =>
        checked((uint)Maths.Ceil(logical * (double)dpi));

    private static bool HasFlag(string[] args, string option) =>
        args.Any(argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int ParseInt(string[] args, string option, int fallback) =>
        GetOption(args, option) is not { } value
            ? fallback
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"{option} requires an integer value.", nameof(args));

    private static uint ParseUInt(string[] args, string option, uint fallback) =>
        GetOption(args, option) is not { } value
            ? fallback
            : uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed != 0
                ? parsed
                : throw new ArgumentException($"{option} requires a positive unsigned integer.", nameof(args));

    private static float ParseFloat(string[] args, string option, float fallback) =>
        GetOption(args, option) is not { } value
            ? fallback
            : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
              float.IsFinite(parsed) && parsed > 0
                ? parsed
                : throw new ArgumentException($"{option} requires a positive finite number.", nameof(args));
}
