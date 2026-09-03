using Delta;
using Delta.XAML;
using Delta.XAML.Contract;

internal static class ValueControlTests
{
    public static void Run()
    {
        SliderUsesCanonicalInputAndTwoWayValueSlot();
        ImageEmitsCanonicalResourceVisual();
        ImageUsesFallbackResourcesAndPreservesAspect();
        ImageRejectsInvalidExternalMetadata();
    }

    private static void ImageRejectsInvalidExternalMetadata()
    {
        var resource = new UiResourceId(new Guid("A217C118-3D21-4F64-A929-CC75A3B51620"));
        var image = new UiImage { Source = resource };
        using var text = new EmptyTextService();
        using var document = new UiDocument(image, text, null, null, new InvalidImageResolver(resource));
        Assert.Throws<InvalidOperationException>(
            () => document.Layout(new float2(32, 32), 1),
            "non-finite external image metadata is rejected before it reaches retained geometry");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => image.Stretch = (UiImageStretch)99,
            "image stretch rejects unknown enum values at the public boundary");
    }

    private static void SliderUsesCanonicalInputAndTwoWayValueSlot()
    {
        var slider = new UiSlider { Width = 100, Height = 20, Minimum = 0, Maximum = 10, Step = 2 };
        using var text = new EmptyTextService();
        using var document = new UiDocument(slider, text);
        document.Layout(new float2(100, 20), 1);
        var down = new UiPointerEvent(
            UiPointerEventKind.ButtonDown,
            UiPointerDeviceKind.Mouse,
            1,
            new float2(75, 10),
            default,
            default,
            UiPointerButton.Primary,
            default,
            0,
            default);
        document.Dispatch(UiInputEvent.FromPointingDevice(down));
        document.Layout(new float2(100, 20), 1);
        Assert.Equal(7.5d, slider.Value, "slider pointer input writes through the canonical typed value slot");

        document.Dispatch(UiInputEvent.FromKey(new(UiKeyEventKind.Down, new UiPhysicalKey(39), default, default, false)));
        document.Layout(new float2(100, 20), 1);
        Assert.Equal(9.5d, slider.Value, "slider keyboard increment uses its typed step");
    }

    private static void ImageEmitsCanonicalResourceVisual()
    {
        var resource = new UiResourceId(new Guid("77CD52B4-5F2F-46D6-86AA-4D19922E73F3"));
        var image = new UiImage { Source = resource };
        using var text = new EmptyTextService();
        using var document = new UiDocument(image, text, null, null, new ImageResolver(resource));
        document.Layout(new float2(200, 100), 1);
        var display = document.BuildDisplayList();

        Assert.Equal(UiImageStatus.Ready, image.Status, "renderer-neutral intrinsic metadata updates image readiness");
        Assert.Equal(new DeltaXAML.Internal.UiSize(64, 32), image.RetainedElement.DesiredSize, "intrinsic image metadata participates in measure");
        Assert.Equal(1, display.Visuals.Length, "one image produces one canonical visual command");
        Assert.Equal(UiVisualKind.Image, display.Visuals[0].Kind, "image remains a resource visual rather than a GPU object");
        Assert.Equal(resource, display.Visuals[0].Resource, "canonical image command preserves the stable resource identity");
    }

    private static void ImageUsesFallbackResourcesAndPreservesAspect()
    {
        var source = new UiResourceId(new Guid("13158D6A-D140-48D8-BA95-ED283E5E4C8B"));
        var placeholder = new UiResourceId(new Guid("77D130A2-FEDA-4867-8F98-03A7C99FE529"));
        var error = new UiResourceId(new Guid("E7331363-3DFD-459E-BDAF-8221445B9D12"));
        var image = new UiImage
        {
            Source = source,
            Placeholder = placeholder,
            ErrorSource = error,
            Stretch = UiImageStretch.UniformToFill,
        };
        using var text = new EmptyTextService();
        var resolver = new MutableImageResolver(source);
        using var document = new UiDocument(image, text, null, null, resolver);
        document.Layout(new float2(100, 100), 1);
        var loading = document.BuildDisplayList();
        Assert.Equal(UiImageStatus.Loading, image.Status, "unready image metadata keeps the control in loading state");
        Assert.Equal(placeholder, loading.Visuals[0].Resource, "loading image emits its neutral placeholder resource");

        resolver.Result = new(64, 32, true, false);
        document.Layout(new float2(100, 100), 1);
        var ready = document.BuildDisplayList();
        Assert.Equal(new float4(-50, 0, 200, 100), ready.Visuals[0].Bounds, "UniformToFill preserves aspect and relies on the retained clip for cropping");
        Assert.Equal(new float4(0, 0, 100, 100), ready.Clips[ready.Visuals[0].Clip.Value].Bounds, "image crop stays inside the canonical clip hierarchy");

        resolver.Result = new(64, 32, false, true);
        document.Layout(new float2(100, 100), 1);
        var failed = document.BuildDisplayList();
        Assert.Equal(UiImageStatus.Error, image.Status, "failed metadata enters explicit error state");
        Assert.Equal(error, failed.Visuals[0].Resource, "failed image emits its neutral error resource");
    }

    private sealed class ImageResolver(UiResourceId expected) : IUiImageMetadataResolver
    {
        public bool TryGetMetadata(UiResourceId image, out UiImageMetadata metadata)
        {
            if (image == expected)
            {
                metadata = new(64, 32, true, false);
                return true;
            }

            metadata = default;
            return false;
        }
    }

    private sealed class MutableImageResolver(UiResourceId expected) : IUiImageMetadataResolver
    {
        internal UiImageMetadata? Result { get; set; }

        public bool TryGetMetadata(UiResourceId image, out UiImageMetadata metadata)
        {
            if (image == expected && Result is { } available)
            {
                metadata = available;
                return true;
            }

            metadata = default;
            return false;
        }
    }

    private sealed class InvalidImageResolver(UiResourceId expected) : IUiImageMetadataResolver
    {
        public bool TryGetMetadata(UiResourceId image, out UiImageMetadata metadata)
        {
            metadata = new(float.NaN, 10, true, false);
            return image == expected;
        }
    }
}
