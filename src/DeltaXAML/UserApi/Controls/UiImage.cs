using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

public sealed class UiImage : UiElement
{
    private Retained.Image StateOwner => (Retained.Image)RetainedElement;

    public UiImage() : base(Retained.UiImageGenerated.Create(), null) { }

    internal UiImage(Retained.Image element) : base(element, null) { }

    public UiResourceId Source
    {
        get => new(StateOwner.Source);
        set => StateOwner.Source = value.Value;
    }

    public UiColor Tint
    {
        get => ToPublicColor(StateOwner.Tint);
        set => StateOwner.Tint = ToRetainedColor(value);
    }

    public UiImageStretch Stretch
    {
        get => (UiImageStretch)StateOwner.Stretch;
        set
        {
            if ((uint)value > (uint)UiImageStretch.UniformToFill)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            StateOwner.Stretch = (byte)value;
        }
    }

    public UiResourceId Placeholder
    {
        get => new(StateOwner.Placeholder);
        set => StateOwner.Placeholder = value.Value;
    }

    public UiResourceId ErrorSource
    {
        get => new(StateOwner.ErrorSource);
        set => StateOwner.ErrorSource = value.Value;
    }

    public UiImageStatus Status => (UiImageStatus)StateOwner.State.Status;
}
