using Delta.Maths;
using Delta.Text;
using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.TipCalc.Generated;

namespace DeltaXaml.Samples.TipCalc;

internal static class Program
{
    private static readonly FontSourceId SampleFontId =
        new(new Guid("B1BD4F0D-4A43-4A15-B5DF-DBF9A5A1A8E3"));

    private static int Main()
    {
        var model = new TipCalcModel
        {
            SubTotal = 42,
            PostTaxTotal = 45,
            TipPercent = 15,
        };

        var fonts = new UiFontCatalog();
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "NotoSans-Regular.ttf");
        fonts.Register("default", SampleFontId, File.ReadAllBytes(fontPath));

        using var textService = new SixLaborsTextService();
        using var page = new TipCalcArtifact(model, textService, fonts);

        page.Document.Layout(new float2(480, 360), 1);
        var initial = page.Document.BuildDisplayList();
        Require(initial.Visuals.Length >= 4, "TipCalc must emit its retained backgrounds.");
        Require(initial.Clips.Length > 0, "TipCalc must emit a root clip.");
        Require(initial.Text.Length >= 8, "TipCalc must emit neutral shaped text requests.");
        var initialTextCount = initial.Text.Length;

        var subTotalEditor = Find<UiTextBox>(page, "SubTotalEditor");
        var postTaxEditor = Find<UiTextBox>(page, "PostTaxTotalEditor");
        var tipEditor = Find<UiTextBox>(page, "TipPercentEditor");
        var slider = Find<UiSlider>(page, "TipPercentSlider");
        var tipAmount = Find<UiTextBlock>(page, "TipAmountText");
        var total = Find<UiTextBlock>(page, "TotalText");

        Require(tipAmount.Text == "$6.30", "Initial tip amount must be formatted by the generated binding.");
        Require(total.Text == "$51.25", "Initial total must be rounded to the nearest quarter.");

        subTotalEditor.SetText("80");
        postTaxEditor.SetText("50");
        tipEditor.SetText("18.5");
        page.Document.Layout(new float2(480, 360), 1);
        var changed = page.Document.BuildDisplayList();

        Require(model.SubTotal == 80, "The subtotal editor must update its source through a generated two-way binding.");
        Require(model.PostTaxTotal == 50, "The post-tax editor must update its source through a generated two-way binding.");
        Require(model.TipPercent == 18.5, "The percentage editor must update its source through a generated two-way binding.");
        Require(slider.Value == 18.5, "The slider must observe the same typed model value.");
        Require(tipAmount.Text == "$14.80", "The tip amount must react to model notifications.");
        Require(total.Text == "$64.75", "The changed total must retain quarter rounding.");
        Require(changed.Text.Length == initialTextCount, "A value update must preserve the retained text slot count.");

        var sliderBounds = slider.Bounds;
        var sliderInput = new UiPointerEvent(
            UiPointerEventKind.ButtonDown,
            UiPointerDeviceKind.Mouse,
            1,
            new float2(sliderBounds.x + (sliderBounds.z * 0.25f), sliderBounds.y + (sliderBounds.w * 0.5f)),
            default,
            default,
            UiPointerButton.Primary,
            new UiPointerButtons(1),
            0,
            default);
        page.Document.Dispatch(UiInputEvent.FromPointingDevice(in sliderInput));
        page.Document.Layout(new float2(480, 360), 1);
        var finalDisplay = page.Document.BuildDisplayList();

        Require(model.TipPercent == 25, "Slider input must write its rounded value through the generated two-way binding.");
        Require(tipEditor.Text == "25", "The percentage editor must observe slider-driven source changes.");
        Require(tipAmount.Text == "$20.00", "Slider input must recalculate the tip amount.");
        Require(total.Text == "$70.00", "Slider input must recalculate the rounded total.");

        Console.WriteLine($"TipCalc: subtotal={model.SubTotal:F2}, tax-total={model.PostTaxTotal:F2}, tip={model.TipPercent:F1}%");
        Console.WriteLine($"Result: tip={tipAmount.Text}, total={total.Text}");
        Console.WriteLine($"Display list: visuals={finalDisplay.Visuals.Length}, clips={finalDisplay.Clips.Length}, text={finalDisplay.Text.Length}");
        return 0;
    }

    private static T Find<T>(TipCalcArtifact page, string name)
        where T : UiElement
    {
        if (page.TryFindName(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Generated namescope entry '{name}' is missing or has the wrong type.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
