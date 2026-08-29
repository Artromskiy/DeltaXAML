using System.Text;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class Image : UiElement
{
    private ImageState _state = new() { Tint = new(255, 255, 255, 255), Stretch = (byte)Delta.XAML.UiImageStretch.Uniform };

    internal ref ImageState State => ref _state;

    internal Image() : base("Image") => AutomationRole = UiAutomationRole.Image;

    internal Guid Source
    {
        get => _state.Resource;
        set
        {
            if (_state.Resource == value)
            {
                return;
            }

            _state.Resource = value;
            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }

    internal UiColor Tint
    {
        get => _state.Tint;
        set
        {
            if (_state.Tint == value)
            {
                return;
            }

            _state.Tint = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal byte Stretch
    {
        get => _state.Stretch;
        set
        {
            if (_state.Stretch == value)
            {
                return;
            }

            _state.Stretch = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid Placeholder
    {
        get => _state.Placeholder;
        set
        {
            if (_state.Placeholder == value)
            {
                return;
            }

            _state.Placeholder = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid ErrorSource
    {
        get => _state.ErrorSource;
        set
        {
            if (_state.ErrorSource == value)
            {
                return;
            }

            _state.ErrorSource = value;
            InvalidateChanged(UiDirtyFlags.Visual);
        }
    }

    internal Guid DisplaySource => _state.Status switch
    {
        (byte)Delta.XAML.UiImageStatus.Error when _state.ErrorSource != Guid.Empty => _state.ErrorSource,
        (byte)Delta.XAML.UiImageStatus.Loading when _state.Placeholder != Guid.Empty => _state.Placeholder,
        _ => _state.Resource,
    };

    internal UiRect ImageBounds
    {
        get
        {
            var width = MathF.Max(0, _state.IntrinsicWidth);
            var height = MathF.Max(0, _state.IntrinsicHeight);
            if ((Delta.XAML.UiImageStretch)_state.Stretch == Delta.XAML.UiImageStretch.Fill || width <= 0 || height <= 0)
            {
                return Bounds;
            }

            var scale = (Delta.XAML.UiImageStretch)_state.Stretch switch
            {
                Delta.XAML.UiImageStretch.None => 1,
                Delta.XAML.UiImageStretch.UniformToFill => MathF.Max(Bounds.Width / width, Bounds.Height / height),
                _ => MathF.Min(Bounds.Width / width, Bounds.Height / height),
            };
            var renderedWidth = width * scale;
            var renderedHeight = height * scale;
            return new(
                Bounds.X + (Bounds.Width - renderedWidth) * 0.5f,
                Bounds.Y + (Bounds.Height - renderedHeight) * 0.5f,
                renderedWidth,
                renderedHeight);
        }
    }

    internal void SetMetadata(float width, float height, byte status)
    {
        if (_state.IntrinsicWidth.Equals(width) && _state.IntrinsicHeight.Equals(height) && _state.Status == status)
        {
            return;
        }

        _state.IntrinsicWidth = MathF.Max(0, width);
        _state.IntrinsicHeight = MathF.Max(0, height);
        _state.Status = status;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }
}
internal sealed class RichTextBlock : UiElement
{
    private RichTextState _state = new()
    {
        Spans = Array.Empty<Delta.XAML.UiTextSpan>(),
        HitRanges = Array.Empty<Delta.XAML.UiInlineHitRange>(),
    };

    internal RichTextBlock() : base("RichTextBlock") => AutomationRole = UiAutomationRole.Text;

    internal ref RichTextState State => ref _state;

    internal ReadOnlyMemory<Delta.XAML.UiTextSpan> Spans
    {
        get => _state.Spans;
        set
        {
            var source = value.Span;
            for (var i = 0; i < source.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(source[i].Text);
                ArgumentException.ThrowIfNullOrWhiteSpace(source[i].FontKey);
                if (!float.IsFinite(source[i].FontSize) || source[i].FontSize <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Rich-text font sizes must be finite and positive.");
                }
            }

            if (source.SequenceEqual(_state.Spans))
            {
                return;
            }

            var shapingChanged = source.Length != _state.Spans.Length;
            if (!shapingChanged)
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var previous = _state.Spans[i];
                    if (!string.Equals(source[i].Text, previous.Text, StringComparison.Ordinal) ||
                        !string.Equals(source[i].FontKey, previous.FontKey, StringComparison.Ordinal) ||
                        !source[i].FontSize.Equals(previous.FontSize))
                    {
                        shapingChanged = true;
                        break;
                    }
                }
            }

            _state.Spans = source.ToArray();
            InvalidateChanged(shapingChanged
                ? UiDirtyFlags.Measure | UiDirtyFlags.Text | UiDirtyFlags.Visual
                : UiDirtyFlags.Text | UiDirtyFlags.Visual);
        }
    }

    internal void SetHitRanges(Delta.XAML.UiInlineHitRange[] ranges, int count)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ranges.Length);
        _state.HitRanges = ranges;
        _state.HitRangeCount = count;
    }

    internal string AutomationText
    {
        get
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _state.Spans.Length; i++)
            {
                builder.Append(_state.Spans[i].Text);
            }

            return builder.ToString();
        }
    }

    internal bool TryGetLink(UiPoint point, out Delta.XAML.UiCommandId command, out string? argument)
        => RichTextHitTestMixin.TryGetLink(ref _state, point, out command, out argument);
}

internal sealed class Overlay : UiElement
{
    private OverlayState _state = new() { IsOpen = true };
    internal ref OverlayState State => ref _state;
    internal Overlay() : base("Overlay") => IsFocusScope = true;
    internal bool IsOpen
    {
        get => _state.IsOpen;
        set
        {
            if (_state.IsOpen == value)
            {
                return;
            }

            _state.IsOpen = value;
            SetParticipation(value ? Delta.XAML.UiParticipation.All : Delta.XAML.UiParticipation.None);
        }
    }
}


internal sealed class TabView : UiElement
{
    private TabViewState _state = new() { SelectedIndex = -1 };
    internal ref TabViewState State => ref _state;
    internal TabView() : base("TabView") => AutomationRole = UiAutomationRole.Tab;
    internal int SelectedIndex { get => _state.SelectedIndex; set => _state.SelectedIndex = value; }
}

internal sealed class Menu : UiElement
{
    private MenuState _state = new() { Layout = new StackPanelState { Orientation = UiOrientation.Vertical } };
    internal ref MenuState State => ref _state;
    internal Menu() : base("Menu")
    {
        IsFocusScope = true;
        AutomationRole = UiAutomationRole.Menu;
    }
}
