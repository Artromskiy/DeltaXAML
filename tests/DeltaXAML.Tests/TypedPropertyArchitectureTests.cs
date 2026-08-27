using DeltaXAML.Internal;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

internal static partial class Program
{
    private static void TypedPropertyStateTests()
    {
        var text = new TextBlock();
        Assert.Equal(14f, text.FontSize, "typed default reaches text state");
        text.SetStyle("FontSize", 18f, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.Equal(18f, text.FontSize, "typed style reaches text state");
        text.SetLocal("FontSize", 20f, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.Equal(20f, text.FontSize, "typed local reaches text state");
        text.Clear("FontSize", UiValueSource.Local);
        Assert.Equal(18f, text.FontSize, "clearing local restores typed style");
        text.Clear("FontSize", UiValueSource.Style);
        Assert.Equal(14f, text.FontSize, "clearing style restores typed default");

        text.SetStyle("Width", 240f, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.Equal(240f, text.Width, "common typed property reaches element state");

        var unchangedVersion = text.LayoutVersion;
        text.SetLocal("Width", 240f, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.Equal(unchangedVersion, text.LayoutVersion, "equal typed value does not invalidate layout");
        Assert.True(text.TryGet("Width", out var unchanged) && unchanged.Source == UiValueSource.Local, "equal typed value updates source metadata");
        text.Clear("Width", UiValueSource.Local);
        Assert.Equal(unchangedVersion, text.LayoutVersion, "clearing equal local value does not invalidate layout");
        Assert.True(text.TryGet("Width", out unchanged) && unchanged.Source == UiValueSource.Style, "clearing equal local restores style metadata");
    }
}

internal static partial class Program
{
    private static void PropertyPrecedenceTests()
    {
        var text = new TextBlock();
        text.SetDefault("Text", "default", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        text.SetStyle("Text", "style", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        var bindingValue = "binding";
        var binding = new UiBindingValue(() => bindingValue, _ => (true, null));
        text.SetBinding("Text", binding, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        text.SetLocal("Text", "local", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        var handle = text.GetHandle("Text");
        Assert.True(text.TrySet(handle, "handle", UiDirtyFlags.Measure | UiDirtyFlags.Visual, out var diagnostic) && diagnostic is null, "handle source applies");
        Assert.True(text.TryGet("Text", out var value) && value.Source == UiValueSource.Handle && Equals(value.UntypedValue, "handle"), "handle wins over local");

        text.Clear("Text", UiValueSource.Handle);
        Assert.True(text.TryGet("Text", out value) && value.Source == UiValueSource.Local, "local follows handle");
        text.Clear("Text", UiValueSource.Local);
        Assert.True(text.TryGet("Text", out value) && value.Source == UiValueSource.Binding, "binding follows local");
        text.Clear("Text", UiValueSource.Binding);
        Assert.True(text.TryGet("Text", out value) && value.Source == UiValueSource.Style, "style follows binding");
        text.Clear("Text", UiValueSource.Style);
        Assert.True(text.TryGet("Text", out value) && value.Source == UiValueSource.Default, "default follows style");
    }
}

internal static partial class Program
{
    private static void HiddenBindingTests()
    {
        var text = new TextBlock();
        var bindingValue = "first";
        var binding = new UiBindingValue(() => bindingValue, _ => (true, null));
        text.SetBinding("Text", binding, UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        var handle = text.GetHandle("Text");
        Assert.True(text.TrySet(handle, "hidden", UiDirtyFlags.Measure | UiDirtyFlags.Visual, out _), "handle hides binding");
        bindingValue = "latest";
        binding.NotifyChanged();
        Assert.True(text.TryGet("Text", out var hidden) && Equals(hidden.UntypedValue, "hidden"), "hidden binding does not replace handle");
        text.Clear("Text", UiValueSource.Handle);
        Assert.True(text.TryGet("Text", out var revealed) && Equals(revealed.UntypedValue, "latest"), "clearing handle reveals latest binding");
    }
}

internal static partial class Program
{
    private static void AnimationSourceTests()
    {
        var text = new TextBlock { Text = "local" };
        text.SetHandle("Text", "handle", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        text.SetAnimation("Text", "animated", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.True(text.TryGet("Text", out var value) && value.Source == UiValueSource.Animation && Equals(value.UntypedValue, "animated"), "animation wins over handle");
        text.Clear("Text", UiValueSource.Animation);
        Assert.True(text.TryGet("Text", out value) && value.Source == UiValueSource.Handle, "clearing animation restores handle");
    }
}

internal static partial class Program
{
    private static void RejectedTypedSourceTests()
    {
        var text = new TextBlock();
        var before = text.LayoutVersion;
        text.SetStyle("FontSize", "not-a-float", UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        Assert.Equal(14f, text.FontSize, "rejected typed style preserves text state");
        Assert.Equal(before, text.LayoutVersion, "rejected typed style does not invalidate layout");
        Assert.True(text.TryGet("FontSize", out var value) && value.Source == UiValueSource.Default, "rejected typed style is not published as effective");

        var numeric = new NumericEditor();
        numeric.Min = 0d;
        numeric.SetStyle("Value", -1d, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        Assert.Equal(0d, numeric.Value, "rejected numeric source preserves value state");
        Assert.True(numeric.TryGet("Value", out value) && value.Source == UiValueSource.Default, "rejected numeric source is not published as effective");
    }
}
