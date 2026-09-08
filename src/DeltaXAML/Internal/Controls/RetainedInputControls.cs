using Delta;
using Delta.XAML.Contract;

using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal sealed class Slider : UiElement
{
    private SliderState _state = new()
    {
        Maximum = 1,
        Step = 0.1,
        Orientation = UiOrientation.Horizontal,
    };

    internal ref SliderState State => ref _state;

    internal Slider() : base("Slider")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.Slider;
        SetDefaultProperty("Minimum", _state.Minimum, UiDirtyFlags.Visual);
        SetDefaultProperty("Maximum", _state.Maximum, UiDirtyFlags.Visual);
        SetDefaultProperty("Value", _state.Value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        SetDefaultProperty("Step", _state.Step, UiDirtyFlags.Visual);
        SetDefaultProperty("Orientation", _state.Orientation, UiDirtyFlags.Measure | UiDirtyFlags.Arrange);
    }

    internal double Minimum { get => _state.Minimum; set => SetLocalProperty("Minimum", value, UiDirtyFlags.Visual); }
    internal double Maximum { get => _state.Maximum; set => SetLocalProperty("Maximum", value, UiDirtyFlags.Visual); }
    internal double Value { get => _state.Value; set => SetLocalProperty("Value", value, UiDirtyFlags.Binding | UiDirtyFlags.Visual); }
    internal double Step { get => _state.Step; set => SetLocalProperty("Step", value, UiDirtyFlags.Visual); }
    internal UiOrientation Orientation { get => _state.Orientation; set => SetLocalProperty("Orientation", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange); }

    internal void SetUserValue(double value)
    {
        value = Maths.Clamp(value, _state.Minimum, _state.Maximum);
        if (HasBinding(nameof(Value)))
        {
            if (_state.Value.Equals(value))
            {
                return;
            }

            _state.Value = value;
            InvalidateChanged(UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        }
        else
        {
            Value = value;
        }

        NotifyBindingTargetChanged(nameof(Value), value);
    }
}

internal sealed class Picker : UiElement
{
    private PickerState _state = new() { SelectedIndex = -1 };
    internal ref PickerState State => ref _state;
    internal Picker() : base("Picker") => Focusable = true;
    internal int SelectedIndex
    {
        get => _state.SelectedIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);

            if (_state.SelectedIndex == value)
            {
                return;
            }

            _state.SelectedIndex = value;
            if (ItemsCollection is { } items)
            {
                items.SelectedIndex = value;
            }

            InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Visual);
        }
    }
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
            if (Children.Count > 1 && Children[1] is Overlay overlay)
            {
                overlay.IsOpen = value;
            }

            InvalidateChanged(UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.HitTest);
        }
    }

    internal bool ProcessPointer(UiElement? target)
    {
        if (target is null)
        {
            return false;
        }

        if (Children.Count > 0 && IsWithin(target, Children[0]))
        {
            IsOpen = !IsOpen;
            return true;
        }

        if (ItemsCollection is { } items && items.SelectTarget(target))
        {
            SelectedIndex = items.SelectedIndex;
            IsOpen = false;
            return true;
        }

        return false;
    }

    internal bool MoveSelection(int delta)
    {
        if (ItemsCollection is not { } items || !items.MoveSelection(delta))
        {
            return false;
        }

        SelectedIndex = items.SelectedIndex;
        return true;
    }

    internal bool SynchronizeSelection()
    {
        if (ItemsCollection is not { } items || items.SelectedIndex == _state.SelectedIndex)
        {
            return false;
        }

        SelectedIndex = items.SelectedIndex;
        return true;
    }

    private CollectionView? ItemsCollection =>
        Children.Count > 1 && Children[1] is Overlay overlay && overlay.Children.Count > 0
            ? overlay.Children[0] as CollectionView
            : null;

    private static bool IsWithin(UiElement candidate, UiElement ancestor)
    {
        for (var current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}


internal class Button : UiElement
{
    private ContentControlState _contentState;
    private ButtonState _state;

    internal ref ContentControlState ContentState => ref _contentState;
    internal ref ButtonState State => ref _state;
    internal ref ButtonState InputState => ref _state;

    public Button(string typeName = "Button") : base(typeName) { Focusable = true; AutomationRole = UiAutomationRole.Button; }
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public event EventHandler? Click;
    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

internal sealed class ToggleButton : UiElement
{
    private ContentControlState _contentState;
    private ButtonState _buttonState;
    private ToggleButtonState _state;

    internal ref ContentControlState ContentState => ref _contentState;
    internal ref ButtonState InputState => ref _buttonState;
    internal ref ToggleButtonState State => ref _state;

    public ToggleButton() : base("ToggleButton") { Focusable = true; AutomationRole = UiAutomationRole.Button; }

    public bool IsChecked => _state.IsChecked;
    public UiElement? Content { get => Children.Count == 0 ? null : Children[0]; set { ClearChildren(); if (value is not null) { Add(value); } } }
    public event EventHandler? Click;
    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

internal class TextBlock : UiElement
{
    private TextBlockState _state;

    internal ref TextBlockState State => ref _state;

    public TextBlock(string typeName = "TextBlock") : base(typeName)
    {
        AutomationRole = UiAutomationRole.Text;
        _state.Text = string.Empty;
        _state.Visual.FontKey = "default";
        _state.Visual.GlyphRunKey = "default";
        _state.Visual.FontSize = 14;
        _state.Visual.Foreground = new(255, 255, 255);
        _state.Layout.HorizontalAlignment = Delta.XAML.UiTextHorizontalAlignment.Left;
        _state.Layout.VerticalAlignment = Delta.XAML.UiTextVerticalAlignment.Top;
        _state.Layout.Wrapping = Delta.XAML.UiTextWrapping.NoWrap;
        _state.Layout.Trimming = Delta.XAML.UiTextTrimming.None;
        _state.Visual.Weight = Delta.XAML.UiFontWeight.Normal;
        _state.Visual.Style = Delta.XAML.UiFontStyle.Normal;
        SetDefaultProperty("Text", _state.Text, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("FontKey", _state.Visual.FontKey, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("FontSize", _state.Visual.FontSize, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("Foreground", _state.Visual.Foreground, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("HorizontalTextAlignment", _state.Layout.HorizontalAlignment, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("VerticalTextAlignment", _state.Layout.VerticalAlignment, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("TextWrapping", _state.Layout.Wrapping, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("TextTrimming", _state.Layout.Trimming, UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("MaxLines", _state.Layout.MaxLines, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("LineHeight", _state.Layout.LineHeight, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual);
        SetDefaultProperty("FontWeight", _state.Visual.Weight, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("FontStyle", _state.Visual.Style, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text);
        SetDefaultProperty("TextDecorations", _state.Visual.Decorations, UiDirtyFlags.Visual | UiDirtyFlags.Text);
    }

    public string Text { get => _state.Text; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("Text", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string FontKey { get => _state.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _state.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_state.Visual.GlyphRunKey == value) { return; } _state.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _state.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _state.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor StrokeColor { get => _state.Visual.StrokeColor; set => SetLocalProperty("StrokeColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float StrokeWidth
    {
        get => _state.Visual.StrokeWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("StrokeWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Delta.XAML.UiTextHorizontalAlignment HorizontalTextAlignment { get => _state.Layout.HorizontalAlignment; set => SetLocalProperty("HorizontalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextVerticalAlignment VerticalTextAlignment { get => _state.Layout.VerticalAlignment; set => SetLocalProperty("VerticalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextWrapping TextWrapping { get => _state.Layout.Wrapping; set => SetLocalProperty("TextWrapping", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextTrimming TextTrimming { get => _state.Layout.Trimming; set => SetLocalProperty("TextTrimming", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public int MaxLines { get => _state.Layout.MaxLines; set => SetLocalProperty("MaxLines", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public float LineHeight { get => _state.Layout.LineHeight; set => SetLocalProperty("LineHeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiFontWeight FontWeight { get => _state.Visual.Weight; set => SetLocalProperty("FontWeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiFontStyle FontStyle { get => _state.Visual.Style; set => SetLocalProperty("FontStyle", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiTextDecorations TextDecorations { get => _state.Visual.Decorations; set => SetLocalProperty("TextDecorations", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }

}

internal class TextBox : UiElement, ITextEditorStateOwner
{
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private TextBlockState _textState;
    private TextBoxState _state;
    private string? _diagnostic;

    internal ref TextBlockState TextState => ref _textState;
    internal ref TextBoxState State => ref _state;
    ref TextBlockState ITextEditorStateOwner.TextState => ref _textState;
    ref TextBoxState ITextEditorStateOwner.EditorState => ref _state;
    UiElement ITextEditorStateOwner.Element => this;
    List<string> ITextEditorStateOwner.UndoHistory => _undo;
    List<string> ITextEditorStateOwner.RedoHistory => _redo;
    string? ITextEditorStateOwner.ValidationDiagnostic { get => _diagnostic; set => _diagnostic = value; }

    public TextBox() : base("TextBox")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.TextBox;
        TextEditorBehaviorMixin.Initialize(this);
    }

    public string Text { get => _textState.Text; set => SetText(value, false); }
    public string FontKey { get => _textState.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _textState.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_textState.Visual.GlyphRunKey == value) { return; } _textState.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _textState.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _textState.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor StrokeColor { get => _textState.Visual.StrokeColor; set => SetLocalProperty("StrokeColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float StrokeWidth
    {
        get => _textState.Visual.StrokeWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("StrokeWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Delta.XAML.UiTextHorizontalAlignment HorizontalTextAlignment { get => _textState.Layout.HorizontalAlignment; set => SetLocalProperty("HorizontalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextVerticalAlignment VerticalTextAlignment { get => _textState.Layout.VerticalAlignment; set => SetLocalProperty("VerticalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextWrapping TextWrapping { get => _textState.Layout.Wrapping; set => SetLocalProperty("TextWrapping", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextTrimming TextTrimming { get => _textState.Layout.Trimming; set => SetLocalProperty("TextTrimming", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public int MaxLines { get => _textState.Layout.MaxLines; set => SetLocalProperty("MaxLines", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public float LineHeight { get => _textState.Layout.LineHeight; set => SetLocalProperty("LineHeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiFontWeight FontWeight { get => _textState.Visual.Weight; set => SetLocalProperty("FontWeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiFontStyle FontStyle { get => _textState.Visual.Style; set => SetLocalProperty("FontStyle", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiTextDecorations TextDecorations { get => _textState.Visual.Decorations; set => SetLocalProperty("TextDecorations", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public string PlaceholderText { get => _state.PlaceholderText; set => SetLocalProperty("PlaceholderText", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public bool IsReadOnly { get => _state.IsReadOnly; set => SetLocalProperty("IsReadOnly", value, UiDirtyFlags.Visual); }
    public bool AcceptsReturn { get => _state.AcceptsReturn; set => SetLocalProperty("AcceptsReturn", value, UiDirtyFlags.Visual); }
    public int MaxLength { get => _state.MaxLength; set => SetLocalProperty("MaxLength", value, UiDirtyFlags.Visual); }
    public int CaretIndex => _state.CaretIndex;
    public int SelectionStart => _state.SelectionStart;
    public int SelectionLength => _state.SelectionLength;
    public int SelectionEnd => _state.SelectionStart + _state.SelectionLength;
    public string? Diagnostic => _diagnostic;
    internal string VisualText => _state.CompositionDisplayText ?? (Text.Length == 0 ? PlaceholderText : Text);
    internal bool IsComposing => _state.CompositionDisplayText is not null;
    internal string? CompositionText => _state.CompositionText;
    internal int CompositionSelectionStart => _state.CompositionSelectionStart;
    internal int CompositionSelectionLength => _state.CompositionSelectionLength;
    public IUiClipboard? Clipboard { get; set; }
    public event EventHandler<TextChangedEventArgs>? TextChanged;

    public void SetText(string text, bool recordUndo = true) => TextEditorBehaviorMixin.SetText(this, text, recordUndo);
    public bool ApplyText(in UiTextInput input) => TextEditorBehaviorMixin.ApplyText(this, in input);
    public bool ApplyComposition(in UiCompositionEvent input) => TextEditorBehaviorMixin.ApplyComposition(this, in input);
    public bool ApplyKey(in UiKeyEvent input) => TextEditorBehaviorMixin.ApplyKey(this, in input);
    public void SelectAll() => TextEditorBehaviorMixin.SelectAll(this);
    public void SetSelection(int start, int length) => TextEditorBehaviorMixin.SetSelection(this, start, length);
    public void Copy() => TextEditorBehaviorMixin.Copy(this);
    public void Cut() => TextEditorBehaviorMixin.Cut(this);
    public bool Paste() => TextEditorBehaviorMixin.Paste(this);
    public bool Undo() => TextEditorBehaviorMixin.Undo(this);
    public bool Redo() => TextEditorBehaviorMixin.Redo(this);
    void ITextEditorStateOwner.RaiseTextChanged(string text) => TextChanged?.Invoke(this, new TextChangedEventArgs(text));
}

internal sealed class NumericEditor : UiElement, ITextEditorStateOwner
{
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private TextBlockState _textState;
    private TextBoxState _editorState;
    private NumericEditorState _state = new() { Min = double.MinValue, Max = double.MaxValue, CommittedText = string.Empty };
    private string? _diagnostic;

    internal ref TextBlockState TextState => ref _textState;
    internal ref TextBoxState EditorState => ref _editorState;
    internal ref NumericEditorState State => ref _state;
    ref TextBlockState ITextEditorStateOwner.TextState => ref _textState;
    ref TextBoxState ITextEditorStateOwner.EditorState => ref _editorState;
    UiElement ITextEditorStateOwner.Element => this;
    List<string> ITextEditorStateOwner.UndoHistory => _undo;
    List<string> ITextEditorStateOwner.RedoHistory => _redo;
    string? ITextEditorStateOwner.ValidationDiagnostic { get => _diagnostic; set => _diagnostic = value; }

    public NumericEditor() : base("NumericEditor")
    {
        Focusable = true;
        AutomationRole = UiAutomationRole.NumericEditor;
        TextEditorBehaviorMixin.Initialize(this);
        SetDefaultProperty("Value", _state.Value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
        SetDefaultProperty("Minimum", _state.Min, UiDirtyFlags.Visual);
        SetDefaultProperty("Maximum", _state.Max, UiDirtyFlags.Visual);
    }

    public string Text { get => _textState.Text; set => SetText(value, false); }
    public string FontKey { get => _textState.Visual.FontKey; set { ArgumentNullException.ThrowIfNull(value); SetLocalProperty("FontKey", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public string GlyphRunKey { get => _textState.Visual.GlyphRunKey; set { ArgumentNullException.ThrowIfNull(value); if (_textState.Visual.GlyphRunKey == value) { return; } _textState.Visual.GlyphRunKey = value; InvalidateChanged(UiDirtyFlags.Visual | UiDirtyFlags.Text); } }
    public float FontSize { get => _textState.Visual.FontSize; set => SetLocalProperty("FontSize", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor Foreground { get => _textState.Visual.Foreground; set => SetLocalProperty("Foreground", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public UiColor StrokeColor { get => _textState.Visual.StrokeColor; set => SetLocalProperty("StrokeColor", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public float StrokeWidth
    {
        get => _textState.Visual.StrokeWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            SetLocalProperty("StrokeWidth", value, UiDirtyFlags.Visual | UiDirtyFlags.Text);
        }
    }
    public Delta.XAML.UiTextHorizontalAlignment HorizontalTextAlignment { get => _textState.Layout.HorizontalAlignment; set => SetLocalProperty("HorizontalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextVerticalAlignment VerticalTextAlignment { get => _textState.Layout.VerticalAlignment; set => SetLocalProperty("VerticalTextAlignment", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextWrapping TextWrapping { get => _textState.Layout.Wrapping; set => SetLocalProperty("TextWrapping", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiTextTrimming TextTrimming { get => _textState.Layout.Trimming; set => SetLocalProperty("TextTrimming", value, UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public int MaxLines { get => _textState.Layout.MaxLines; set => SetLocalProperty("MaxLines", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public float LineHeight { get => _textState.Layout.LineHeight; set => SetLocalProperty("LineHeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual); }
    public Delta.XAML.UiFontWeight FontWeight { get => _textState.Visual.Weight; set => SetLocalProperty("FontWeight", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiFontStyle FontStyle { get => _textState.Visual.Style; set => SetLocalProperty("FontStyle", value, UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public Delta.XAML.UiTextDecorations TextDecorations { get => _textState.Visual.Decorations; set => SetLocalProperty("TextDecorations", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public string PlaceholderText { get => _editorState.PlaceholderText; set => SetLocalProperty("PlaceholderText", value, UiDirtyFlags.Visual | UiDirtyFlags.Text); }
    public bool IsReadOnly { get => _editorState.IsReadOnly; set => SetLocalProperty("IsReadOnly", value, UiDirtyFlags.Visual); }
    public bool AcceptsReturn { get => _editorState.AcceptsReturn; set => SetLocalProperty("AcceptsReturn", value, UiDirtyFlags.Visual); }
    public int MaxLength { get => _editorState.MaxLength; set => SetLocalProperty("MaxLength", value, UiDirtyFlags.Visual); }
    public int CaretIndex => _editorState.CaretIndex;
    public int SelectionStart => _editorState.SelectionStart;
    public int SelectionLength => _editorState.SelectionLength;
    public int SelectionEnd => _editorState.SelectionStart + _editorState.SelectionLength;
    public string? Diagnostic => _diagnostic;
    internal string VisualText => _editorState.CompositionDisplayText ?? (Text.Length == 0 ? PlaceholderText : Text);
    internal bool IsComposing => _editorState.CompositionDisplayText is not null;
    internal string? CompositionText => _editorState.CompositionText;
    internal int CompositionSelectionStart => _editorState.CompositionSelectionStart;
    internal int CompositionSelectionLength => _editorState.CompositionSelectionLength;
    public IUiClipboard? Clipboard { get; set; }
    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public double Value => _state.Value;
    public double Min { get => _state.Min; set => SetLocalProperty("Minimum", value, UiDirtyFlags.Visual); }
    public double Max { get => _state.Max; set => SetLocalProperty("Maximum", value, UiDirtyFlags.Visual); }
    public bool HasValidationError => _diagnostic is not null;
    public bool IsDirty => Text != _state.CommittedText;

    public void SetText(string text, bool recordUndo = true) => TextEditorBehaviorMixin.SetText(this, text, recordUndo);
    public bool ApplyText(in UiTextInput input) => TextEditorBehaviorMixin.ApplyText(this, in input);
    public bool ApplyComposition(in UiCompositionEvent input) => TextEditorBehaviorMixin.ApplyComposition(this, in input);
    public void SelectAll() => TextEditorBehaviorMixin.SelectAll(this);
    public void SetSelection(int start, int length) => TextEditorBehaviorMixin.SetSelection(this, start, length);
    public void Copy() => TextEditorBehaviorMixin.Copy(this);
    public void Cut() => TextEditorBehaviorMixin.Cut(this);
    public bool Paste() => TextEditorBehaviorMixin.Paste(this);
    public bool Undo() => TextEditorBehaviorMixin.Undo(this);
    public bool Redo() => TextEditorBehaviorMixin.Redo(this);
    public void Initialize(double value) => SetLocalProperty("Value", value, UiDirtyFlags.Binding | UiDirtyFlags.Visual);
    public bool TryCommitText(string text) { SetText(text); return TryCommit(); }
    public bool Increment(double step = 1) => Adjust(step);
    public bool Decrement(double step = 1) => Adjust(-step);

    public bool ApplyKey(in UiKeyEvent input)
    {
        return UiNumericEditorGenerated.ProcessKey(ref _state, in input, Text.Length) switch
        {
            UiTextEditAction.Increment => Increment(),
            UiTextEditAction.Decrement => Decrement(),
            _ => TextEditorBehaviorMixin.ApplyKey(this, in input),
        };
    }

    public bool TryCommit()
    {
        if (!UiNumericEditorGenerated.TryCommit(ref _state, Text, Min, Max, out var formatted, out _diagnostic))
        {
            SetInvalid(true);
            return false;
        }

        SetText(formatted, false);
        _diagnostic = null;
        SetInvalid(false);
        return true;
    }

    public void CancelEdit()
    {
        SetText(_state.CommittedText ?? string.Empty, false);
        _diagnostic = null;
        SetInvalid(false);
    }

    public bool TryApplyValue(string text, out string? error)
    {
        if (UiNumericEditorGenerated.TryCommit(ref _state, text, Min, Max, out var formatted, out _diagnostic))
        {
            SetText(formatted, false);
            _diagnostic = null;
            SetInvalid(false);
            error = null;
            return true;
        }

        SetInvalid(true);
        error = _diagnostic;
        return false;
    }

    private bool Adjust(double delta)
    {
        if (!UiNumericEditorGenerated.TryAdjust(ref _state, delta, out _diagnostic))
        {
            SetInvalid(true);
            return false;
        }

        _state.CommittedText = UiNumericEditorGenerated.Format(_state.Value);
        SetText(_state.CommittedText, false);
        _diagnostic = null;
        SetInvalid(false);
        return true;
    }

    void ITextEditorStateOwner.RaiseTextChanged(string text) => TextChanged?.Invoke(this, new TextChangedEventArgs(text));
}
