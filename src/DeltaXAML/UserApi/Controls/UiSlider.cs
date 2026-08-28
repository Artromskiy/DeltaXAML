using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiSlider : UiElement
{
    private Retained.Slider StateOwner => (Retained.Slider)RetainedElement;

    public UiSlider() : base(Retained.UiSliderGenerated.Create(), null) { }

    internal UiSlider(Retained.Slider element) : base(element, null) { }

    public double Minimum { get => StateOwner.Minimum; set => StateOwner.Minimum = value; }

    public double Maximum { get => StateOwner.Maximum; set => StateOwner.Maximum = value; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1721:Property names should not match get methods",
        Justification = "Value is the conventional slider API; the inherited generic GetValue method serves tooling properties.")]
    public double Value { get => StateOwner.Value; set => StateOwner.Value = value; }

    public double Step { get => StateOwner.Step; set => StateOwner.Step = value; }

    public UiOrientation Orientation { get => (UiOrientation)StateOwner.Orientation; set => StateOwner.Orientation = (Retained.UiOrientation)value; }
}
