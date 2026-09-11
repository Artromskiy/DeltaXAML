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

    private static readonly UiColor Outline = new(255, 138, 0);

    internal static void Register(UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources.Set(SpectrumGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(SelectionGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0, 33)),
                new(1, new UiColor(216, 60, 165, 31)),
            }));
        resources.Set(SoftSpectrumGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0, 46)),
                new(1, new UiColor(121, 41, 216, 51)),
            }));
        resources.Set(OrangeRedGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(1, new UiColor(255, 75, 49)),
            }));
        resources.Set(MagentaVioletGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(CyanGreenGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(40, 168, 189)),
                new(1, new UiColor(102, 196, 60)),
            }));
        resources.Set(ActiveGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
        resources.Set(SwatchGradient, RelativeGradient(
            110,
            new UiGradientStop[]
            {
                new(0, new UiColor(255, 138, 0)),
                new(0.3f, new UiColor(255, 75, 49)),
                new(0.56f, new UiColor(216, 60, 165)),
                new(1, new UiColor(121, 41, 216)),
            }));
    }

    private static UiLinearGradient RelativeGradient(float angleDegrees, params UiGradientStop[] stops) =>
        UiLinearGradient.Relative(angleDegrees, stops).WithOutline(Outline);

    private static UiResourceId Resource(string value) => new(Guid.Parse(value));
}
