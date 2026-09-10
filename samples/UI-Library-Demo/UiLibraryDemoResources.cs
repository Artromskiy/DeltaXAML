using Delta.XAML;
using Delta.XAML.Contract;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal static class UiLibraryDemoResources
{
    internal static readonly UiResourceId SpectrumGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1001");
    internal static readonly UiResourceId SelectionGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1002");
    internal static readonly UiResourceId SoftSpectrumGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1003");
    internal static readonly UiResourceId OrangeRedGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1004");
    internal static readonly UiResourceId MagentaVioletGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1005");
    internal static readonly UiResourceId CyanGreenGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1006");
    internal static readonly UiResourceId ActiveGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1007");
    internal static readonly UiResourceId SwatchGradient = Resource("7F5C2A7E-7A7D-4B1F-8C3D-9A4F2B6E1008");

    private static readonly UiResourceId[] GradientResources =
    [
        SpectrumGradient,
        SelectionGradient,
        SoftSpectrumGradient,
        OrangeRedGradient,
        MagentaVioletGradient,
        CyanGreenGradient,
        ActiveGradient,
        SwatchGradient,
    ];

    internal static ReadOnlySpan<UiResourceId> Gradients => GradientResources;

    internal static void Register(UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources.Set(SpectrumGradient, new UiLinearGradient(
            658,
            130,
            790,
            130,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(SelectionGradient, new UiLinearGradient(
            971,
            434,
            1105,
            434,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0, 33)),
                new(1, new UiColor(216, 60, 165, 31)),
            }));
        resources.Set(SoftSpectrumGradient, new UiLinearGradient(
            353,
            887,
            622,
            887,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0, 46)),
                new(1, new UiColor(121, 41, 216, 51)),
            }));
        resources.Set(OrangeRedGradient, new UiLinearGradient(
            893,
            1042,
            1250,
            1042,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(1, new UiColor(255, 75, 49)),
            }));
        resources.Set(MagentaVioletGradient, new UiLinearGradient(
            893,
            1069,
            1250,
            1069,
            new UiGradientStop[]
            {
                new(0, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(CyanGreenGradient, new UiLinearGradient(
            893,
            1096,
            1250,
            1096,
            new UiGradientStop[]
            {
                new(0, new UiColor(40, 168, 189)),
                new(1, new UiColor(102, 196, 60)),
            }));
        resources.Set(ActiveGradient, new UiLinearGradient(
            713,
            207,
            777,
            207,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(SwatchGradient, new UiLinearGradient(
            262,
            830,
            302,
            830,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
    }

    private static UiResourceId Resource(string value) => new(Guid.Parse(value));
}
