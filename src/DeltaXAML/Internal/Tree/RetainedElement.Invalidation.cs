using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal partial class UiElement
{
    public void Invalidate(UiDirtyFlags flags)
    {
        InvalidateCore(flags, false);
    }

    private void SetGridSlot(ref int field, int value, byte flag, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        if (field == value && (_state.GridPlacementFlags & flag) != 0)
        {
            return;
        }

        field = value;
        _state.GridPlacementFlags |= flag;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    private void SetGridSpan(ref int field, int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        if (field == value)
        {
            return;
        }

        field = value;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    private void AdvanceDetachedRelationVersion()
    {
        _relationVersion++;
        for (var i = 0; i < _detachedChildren.Count; i++)
        {
            _detachedChildren[i].AdvanceDetachedRelationVersion();
        }
    }

    internal void InvalidateChanged(UiDirtyFlags flags)
    {
        InvalidateCore(flags, true);
    }

    private void InvalidateCore(UiDirtyFlags flags, bool changed)
    {
        if (flags == UiDirtyFlags.None)
        {
            return;
        }

        var current = this;
        var propagatedFlags = flags;
        while (propagatedFlags != UiDirtyFlags.None)
        {
            var newFlags = propagatedFlags & ~current.DirtyFlags;
            current.DirtyFlags |= propagatedFlags;
            if (!changed && newFlags == UiDirtyFlags.None)
            {
                return;
            }

            var versionFlags = changed ? propagatedFlags : newFlags;
            var versionedFlags = versionFlags & ~UiDirtyFlags.BindingSubtree;
            if (versionedFlags != UiDirtyFlags.None)
            {
                current._outputVersion++;
                if ((versionedFlags & UiDirtyFlags.Tree) != 0)
                {
                    current._treeVersion++;
                }

                if ((versionedFlags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Style | UiDirtyFlags.Resource)) != 0)
                {
                    current._layoutVersion++;
                }

                if ((versionedFlags & UiDirtyFlags.Text) != 0)
                {
                    current._textVersion++;
                    current._textRunVersion++;
                }
            }

            var parentFlags = UiDirtyFlags.None;
            if ((versionedFlags & (UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Style | UiDirtyFlags.Resource)) != 0)
            {
                parentFlags |= UiDirtyFlags.Measure;
            }

            if ((versionedFlags & UiDirtyFlags.Tree) != 0)
            {
                parentFlags |= UiDirtyFlags.Tree;
            }

            if ((versionedFlags & UiDirtyFlags.Visual) != 0)
            {
                parentFlags |= UiDirtyFlags.Visual;
            }

            if ((versionedFlags & UiDirtyFlags.Text) != 0)
            {
                parentFlags |= UiDirtyFlags.Visual;
            }

            if ((versionedFlags & UiDirtyFlags.HitTest) != 0)
            {
                parentFlags |= UiDirtyFlags.HitTest;
            }

            if ((versionedFlags & UiDirtyFlags.Style) != 0)
            {
                parentFlags |= UiDirtyFlags.Style;
            }

            if ((versionFlags & (UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree)) != 0)
            {
                parentFlags |= UiDirtyFlags.BindingSubtree;
            }

            current = current.Parent as UiElement;
            propagatedFlags = parentFlags;
            if (current is null)
            {
                return;
            }
        }
    }
    public void SetHovered(bool value) { if (IsHovered != value) { IsHovered = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    public void SetPressed(bool value) => UiDescriptorCatalog.SetPressed(this, value);
    public void SetFocused(bool value) { if (IsFocused != value) { IsFocused = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    public void SetInvalid(bool value) { if (IsInvalid != value) { IsInvalid = value; InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual); } }
    internal void SetCustomVisual(Guid visualType, Guid resource, UiColor color)
    {
        if (visualType == Guid.Empty)
        {
            throw new ArgumentException("A custom visual identity is required.", nameof(visualType));
        }

        if (_state.CustomVisualType == visualType && _state.CustomVisualResource == resource && _state.CustomVisualColor == color)
        {
            return;
        }

        _state.CustomVisualType = visualType;
        _state.CustomVisualResource = resource;
        _state.CustomVisualColor = color;
        InvalidateChanged(UiDirtyFlags.Visual);
    }

    internal void ClearCustomVisual()
    {
        if (!HasCustomVisual)
        {
            return;
        }

        _state.CustomVisualType = Guid.Empty;
        _state.CustomVisualResource = Guid.Empty;
        _state.CustomVisualColor = default;
        InvalidateChanged(UiDirtyFlags.Visual);
    }
    public void SetParticipation(Delta.XAML.UiParticipation value)
    {
        if (Participation == value)
        {
            return;
        }

        Participation = value;
        InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.HitTest);
    }
    internal bool CanSkipMeasure(UiSize available) =>
        _hasMeasured && (DirtyFlags & UiDirtyFlags.Measure) == 0 && _measuredAvailable == available;

    internal bool NeedsMeasure(UiSize available) => !CanSkipMeasure(available);

    internal void CompleteMeasure(UiSize available, UiSize desired)
    {
        DesiredSize = desired;
        _measuredAvailable = available;
        _hasMeasured = true;
        DirtyFlags &= ~UiDirtyFlags.Measure;
        DirtyFlags |= UiDirtyFlags.Arrange;
    }

    internal bool CanSkipArrange(UiRect bounds, UiRect clip) =>
        _hasArranged && (DirtyFlags & UiDirtyFlags.Arrange) == 0 && _arrangedBounds == bounds && _arrangedClip == clip;

    internal bool NeedsArrange(UiRect bounds, UiRect clip) => !CanSkipArrange(bounds, clip);

    internal void SetArrangeFrame(UiRect bounds, UiRect clip)
    {
        Bounds = bounds;
        Clip = clip;
    }

    internal void SetNonParticipatingArrange(UiRect clip)
    {
        Bounds = default;
        Clip = clip;
    }

    internal void CompleteArrange(UiRect bounds, UiRect clip)
    {
        _arrangedBounds = bounds;
        _arrangedClip = clip;
        _hasArranged = true;
        DirtyFlags &= ~UiDirtyFlags.Arrange;
    }

    internal void CompleteVisualExtraction() => DirtyFlags &= ~(UiDirtyFlags.Tree | UiDirtyFlags.Visual | UiDirtyFlags.Text);
    internal bool IsStyleDirty => (DirtyFlags & UiDirtyFlags.Style) != 0;
    internal bool NeedsResourceStage => (DirtyFlags & UiDirtyFlags.Resource) != 0;
    internal void ApplyResourceStage()
    {
        _properties.ApplyPendingResources();
        DirtyFlags &= ~UiDirtyFlags.Resource;
    }
    internal void CompleteStyleStage() => DirtyFlags &= ~UiDirtyFlags.Style;
    internal bool NeedsVisualExtraction => (DirtyFlags & (UiDirtyFlags.Tree | UiDirtyFlags.Style | UiDirtyFlags.Binding | UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual | UiDirtyFlags.Resource | UiDirtyFlags.Text)) != 0;
    internal int DisplayClipIndex => _displayClipIndex;
    internal int DisplayVisualIndex => _displayVisualIndex;
    internal int DisplayTextIndex => _displayTextIndex;
    internal int DisplayOwnTextCount => _displayOwnTextCount;
    internal int DisplayClipCount => _displayClipCount;
    internal int DisplayVisualCount => _displayVisualCount;
    internal int DisplayTextCount => _displayTextCount;
    internal void SetDisplayRange(int clipIndex, int visualIndex, int textIndex, int ownTextCount = 0)
    {
        _displayClipIndex = clipIndex;
        _displayVisualIndex = visualIndex;
        _displayTextIndex = textIndex;
        _displayOwnTextCount = ownTextCount;
    }
    internal void SetDisplaySubtreeCounts(int clips, int visuals, int text)
    {
        _displayClipCount = clips;
        _displayVisualCount = visuals;
        _displayTextCount = text;
    }
    internal void ClearDisplayRange()
    {
        SetDisplayRange(-1, -1, -1, 0);
        SetDisplaySubtreeCounts(0, 0, 0);
    }

    public uint TextVersion => _textVersion;
    public uint LayoutVersion => _layoutVersion;
    internal uint OutputVersion => _outputVersion;
    internal event Action<string>? EffectivePropertyChanged;
    internal Delta.XAML.UiStyle? AppliedStyle => _appliedStyle;
    internal Delta.XAML.UiStyleState AppliedStyleState => _appliedStyleState;
    internal int AppliedStyleVersion => _appliedStyleVersion;
    internal Guid CompiledStyleId => _compiledStyleId;
    internal Delta.XAML.UiTemplateId CompiledTemplateId => _compiledTemplateId;
    public float LayoutScale => _layoutScale;
    public float DpiScale => _dpiScale;
    public void SetLayoutScale(float scale) => ApplyLayoutScale(scale);

    internal void ApplyLayoutScale(float scale)
    {
        if (Math.Abs(_dpiScale - scale) > float.Epsilon)
        {
            _dpiScale = scale;
            _dpiVersion++;
            _textRunVersion++;
            _outputVersion++;
            _layoutVersion++;
            DirtyFlags |= UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual;
        }

        _layoutScale = scale;
    }
    internal uint TextRunVersion => _textRunVersion;
}
