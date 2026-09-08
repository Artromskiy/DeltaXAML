using System.Diagnostics.CodeAnalysis;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

/// <summary>Canonical retained state owner addressed by the document node store.</summary>
internal partial class UiElement
{
    private static uint _nextId;
    private static uint _nextGeneration;
    private readonly List<UiElement> _detachedChildren = new();
    private readonly UiElementChildrenView _children;
    private readonly string _typeName;
    private readonly List<UiBindingSpec> _bindingSpecs = new();
    private readonly Dictionary<string, UiInterpretedBinding> _bindingRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiExternalBindingRuntime> _externalBindingRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IUiCompiledBindingRuntime> _compiledBindingRuntimes = new(StringComparer.Ordinal);
    private readonly UiPropertyStore _properties;
    private UiNodeStore? _nodeStore;
    private UiElementState _state = new()
    {
        Width = float.NaN,
        Height = float.NaN,
        HorizontalAlignment = Delta.XAML.UiHorizontalAlignment.Stretch,
        VerticalAlignment = Delta.XAML.UiVerticalAlignment.Stretch,
        BlendMode = Delta.XAML.Contract.UiBlendMode.PremultipliedAlpha,
        IsEnabled = true,
        GridRowSpan = 1,
        GridColumnSpan = 1,
    };
    private object? _bindingContext;
    private bool _hasExplicitBindingContext;
    private bool _bindingStageManaged;
    private float _layoutScale = 1f;
    private float _dpiScale = 1f;
    private uint _layoutVersion;
    private uint _dpiVersion;
    private uint _textVersion;
    private uint _textRunVersion;
    private uint _treeVersion;
    private uint _relationVersion;
    private uint _outputVersion;
    private Delta.XAML.UiStyle? _appliedStyle;
    private Delta.XAML.UiStyleState _appliedStyleState;
    private int _appliedStyleVersion = -1;
    private Guid _compiledStyleId;
    private Delta.XAML.UiTemplateId _compiledTemplateId;
    private string? _styleKey;
    private string? _templateKey;
    private UiSize _measuredAvailable;
    private UiRect _arrangedBounds;
    private UiRect _arrangedClip;
    private bool _hasMeasured;
    private bool _hasArranged;
    private bool _runtimeDisposed;
    private int _displayClipIndex = -1;
    private int _displayVisualIndex = -1;
    private int _displayTextIndex = -1;
    private int _displayOwnTextCount;
    private int _displayClipCount;
    private int _displayVisualCount;
    private int _displayTextCount;
    internal ref UiElementState CommonState => ref _state;
    public UiElement(string typeName = "Element")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        _typeName = typeName;
        Id = new(++_nextId);
        Generation = ++_nextGeneration;
        _children = new(this);
        _properties = new(this);
        _properties.InitializeDefault("Width", _state.Width, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Height", _state.Height, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Margin", _state.Margin, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("HorizontalAlignment", _state.HorizontalAlignment, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("VerticalAlignment", _state.VerticalAlignment, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("Background", _state.Background, UiDirtyFlags.Visual);
        _properties.InitializeDefault("BorderColor", _state.BorderColor, UiDirtyFlags.Visual);
        _properties.InitializeDefault("EffectSet", _state.EffectSet, UiDirtyFlags.Visual);
        _properties.InitializeDefault("BlendMode", _state.BlendMode, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        _properties.InitializeDefault("BorderWidth", _state.BorderWidth, UiDirtyFlags.Visual);
        _properties.InitializeDefault("BorderWidthUnits", _state.BorderWidthUnits, UiDirtyFlags.Visual);
        _properties.InitializeDefault("CornerRadius", _state.CornerRadius, UiDirtyFlags.Visual);
        _properties.InitializeDefault("Padding", _state.Padding, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsEnabled", _state.IsEnabled, UiDirtyFlags.Visual);
        _properties.InitializeDefault("IsSelected", _state.IsSelected, UiDirtyFlags.Visual);
    }
    public UiElementId Id { get; }
    public uint Generation { get; }
    internal uint TreeVersion => _treeVersion;
    internal uint RelationVersion => _relationVersion;
    public string TypeName => _typeName;
    public UiElement? Parent => _nodeStore is { } store ? store.GetLogicalParent(this) : _detachedParent;
    public IReadOnlyList<UiElement> Children => _children;
    private UiElement? _detachedParent;
    internal UiNodeStore? NodeStore => _nodeStore;
    internal List<UiElement> DetachedChildren => _detachedChildren;
    public UiVisibility Visibility { get; set; } = UiVisibility.Visible; public bool Focusable { get; set; }
    public Delta.XAML.UiParticipation Participation { get; private set; } = Delta.XAML.UiParticipation.All;
    internal bool ParticipatesIn(Delta.XAML.UiParticipation participation) => (Participation & participation) == participation;
    public float Width
    {
        get => _state.Width;
        set
        {
            if (!ElementPlacementMixin.IsValidDimension(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Width must be NaN or finite and non-negative.");
            }

            SetLocalProperty("Width", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    public float Height
    {
        get => _state.Height;
        set
        {
            if (!ElementPlacementMixin.IsValidDimension(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Height must be NaN or finite and non-negative.");
            }

            SetLocalProperty("Height", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    public UiThickness Margin
    {
        get => _state.Margin;
        set
        {
            if (!ElementPlacementMixin.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Margin must contain only finite values.");
            }

            SetLocalProperty("Margin", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.UiHorizontalAlignment HorizontalAlignment
    {
        get => _state.HorizontalAlignment;
        set
        {
            if (value is Delta.XAML.UiHorizontalAlignment.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetLocalProperty("HorizontalAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.UiVerticalAlignment VerticalAlignment
    {
        get => _state.VerticalAlignment;
        set
        {
            if (value is Delta.XAML.UiVerticalAlignment.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetLocalProperty("VerticalAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    public UiRect Bounds { get; protected set; }
    public UiRect Clip { get; protected set; }
    public UiSize DesiredSize { get; protected set; }
    public UiColor Background { get => _state.Background; set => SetLocalProperty("Background", value, UiDirtyFlags.Visual); }
    public UiColor BorderColor { get => _state.BorderColor; set => SetLocalProperty("BorderColor", value, UiDirtyFlags.Visual); }
    public Delta.XAML.Contract.UiEffectSet EffectSet
    {
        get => _state.EffectSet;
        set
        {
            if (value != Delta.XAML.Contract.UiEffectSet.None && !value.IsValid)
            {
                throw new ArgumentException("EffectSet must be empty or a valid prepared effect resource.", nameof(value));
            }

            SetLocalProperty("EffectSet", value, UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.Contract.UiBlendMode BlendMode
    {
        get => _state.BlendMode;
        set
        {
            if (value is not (Delta.XAML.Contract.UiBlendMode.Opaque or Delta.XAML.Contract.UiBlendMode.Alpha or
                Delta.XAML.Contract.UiBlendMode.PremultipliedAlpha or Delta.XAML.Contract.UiBlendMode.Additive or
                Delta.XAML.Contract.UiBlendMode.Multiply))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetLocalProperty("BlendMode", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public float BorderWidth
    {
        get => _state.BorderWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("BorderWidth", value, UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.Contract.PaintUnits BorderWidthUnits
    {
        get => _state.BorderWidthUnits;
        set
        {
            if (value is not (Delta.XAML.Contract.PaintUnits.Logical or Delta.XAML.Contract.PaintUnits.Device))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetLocalProperty("BorderWidthUnits", value, UiDirtyFlags.Visual);
        }
    }
    public Delta.XAML.UiCornerRadii CornerRadius
    {
        get => _state.CornerRadius;
        set
        {
            if (!value.IsFiniteNonNegative) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("CornerRadius", value, UiDirtyFlags.Visual);
        }
    }
    internal bool HasCustomVisual => _state.CustomVisualType != Guid.Empty;
    internal Guid CustomVisualTypeId => _state.CustomVisualType;
    internal Guid CustomVisualResourceId => _state.CustomVisualResource;
    internal UiColor CustomVisualColor => _state.CustomVisualColor;
    public bool IsEnabled { get => _state.IsEnabled; set => SetLocalProperty("IsEnabled", value, UiDirtyFlags.Visual); }
    public bool IsHovered { get; private set; }
    public bool IsPressed => UiDescriptorCatalog.IsPressed(this);
    public bool IsSelected
    {
        get => _state.IsSelected;
        set
        {
            if (_state.IsSelected != value)
            {
                SetLocalProperty("IsSelected", value, UiDirtyFlags.Visual);
            }
        }
    }
    public bool IsInvalid { get; protected set; }
    public string? StyleKey
    {
        get => _styleKey;
        set
        {
            if (string.Equals(_styleKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _styleKey = value;
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }
    public string? TemplateKey
    {
        get => _templateKey;
        set
        {
            if (string.Equals(_templateKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _templateKey = value;
            _compiledTemplateId = default;
            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
        }
    }
    public string? AutomationName { get; set; }
    public UiAutomationRole AutomationRole { get; set; } = UiAutomationRole.Generic;
    public UiThickness Padding
    {
        get => _state.Padding;
        set
        {
            if (!ElementPlacementMixin.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Padding must contain only finite values.");
            }

            SetLocalProperty("Padding", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        }
    }
    internal Delta.XAML.UiGestureKind Gestures
    {
        get => (Delta.XAML.UiGestureKind)_state.GestureBits;
        set => _state.GestureBits = (ulong)value;
    }
    internal Delta.XAML.UiCommandId Command
    {
        get => new(_state.CommandId);
        set => _state.CommandId = value.Value;
    }
    internal Delta.XAML.UiKeyGesture CommandKey
    {
        get => new(new(_state.CommandPhysicalKey), new(_state.CommandModifiers));
        set
        {
            _state.CommandPhysicalKey = value.Key.Value;
            _state.CommandModifiers = value.Modifiers.Bits;
        }
    }
    internal bool IsFocusScope
    {
        get => _state.IsFocusScope;
        set => _state.IsFocusScope = value;
    }
    internal int GridRow => _state.GridRow;
    internal int GridColumn => _state.GridColumn;
    internal int GridRowSpan => _state.GridRowSpan;
    internal int GridColumnSpan => _state.GridColumnSpan;
    internal bool HasGridRow => (_state.GridPlacementFlags & 1) != 0;
    internal bool HasGridColumn => (_state.GridPlacementFlags & 2) != 0;
    internal int CollectionIndex => _state.HasCollectionIndex ? _state.CollectionIndex : -1;
    internal void SetGridRow(int value) => SetGridSlot(ref _state.GridRow, value, 1, nameof(GridRow));
    internal void SetGridColumn(int value) => SetGridSlot(ref _state.GridColumn, value, 2, nameof(GridColumn));
    internal void SetGridRowSpan(int value) => SetGridSpan(ref _state.GridRowSpan, value, nameof(GridRowSpan));
    internal void SetGridColumnSpan(int value) => SetGridSpan(ref _state.GridColumnSpan, value, nameof(GridColumnSpan));
    internal void SetCollectionIndex(int value)
    {
        if (value < 0)
        {
            _state.CollectionIndex = 0;
            _state.HasCollectionIndex = false;
            return;
        }

        _state.CollectionIndex = value;
        _state.HasCollectionIndex = true;
    }
    internal UiDirtyFlags DirtyFlags { get; set; } = UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual;
    public UiAutomationMetadata Automation => new(AutomationName ?? TypeName, AutomationRole, GetAutomationValueText(), IsEnabled, IsInvalid);
    public UiStateSnapshot VisualState => new(IsEnabled ? IsInvalid ? UiVisualState.Invalid : IsPressed ? UiVisualState.Pressed : IsHovered ? UiVisualState.Hover : IsSelected ? UiVisualState.Selected : IsFocused ? UiVisualState.Focused : UiVisualState.Normal : UiVisualState.Disabled, IsEnabled, IsInvalid, IsSelected, IsFocused, IsHovered, IsPressed);
    public bool IsFocused { get; private set; }
}
