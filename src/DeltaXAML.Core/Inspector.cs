using DeltaXAML.Abstractions;

using UiDirtyFlags = DeltaXAML.Abstractions.UiDirtyMask;

namespace DeltaXAML.Core;

public sealed class ComponentInspector : ContentControl
{
    private readonly StackPanel _root = new() { Orientation = UiOrientation.Vertical };
    private readonly Border _toolbar = new();
    private readonly Grid _body = new();
    private readonly Border _hierarchy = new();
    private readonly Border _viewport = new();
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _rows = new() { Orientation = UiOrientation.Vertical };
    private readonly Border _status = new();
    private readonly Dictionary<string, InspectorRow> _rowByKey = new(StringComparer.Ordinal);
    private readonly List<string> _activeKeys = new();
    private IInspectorItemSource? _source;
    public ComponentInspector()
    {
        StyleKey = "ShellRoot";
        _toolbar.StyleKey = "Toolbar";
        _hierarchy.StyleKey = "Sidebar";
        _viewport.StyleKey = "Inspector";
        _status.StyleKey = "Status";
        var toolbarRow = new StackPanel { Orientation = UiOrientation.Horizontal };
        toolbarRow.Add(new TextBlock { Text = "Component Inspector", StyleKey = "Title", AutomationName = "Inspector Title" });
        toolbarRow.Add(new TextBlock { Text = "live retained UI", StyleKey = "Muted", AutomationName = "Inspector Subtitle" });
        _toolbar.Add(toolbarRow);
        var hierarchyContent = new StackPanel { Orientation = UiOrientation.Vertical };
        hierarchyContent.Add(new TextBlock { Text = "Hierarchy", StyleKey = "Title" });
        hierarchyContent.Add(new TextBlock { Text = "Placeholder tree", StyleKey = "Muted" });
        _hierarchy.Add(hierarchyContent);
        var viewportContent = new StackPanel { Orientation = UiOrientation.Vertical };
        viewportContent.Add(new TextBlock { Text = "Viewport", StyleKey = "Title" });
        viewportContent.Add(new TextBlock { Text = "Renderer-neutral scene placeholder", StyleKey = "Muted" });
        _viewport.Add(viewportContent);
        _scroll.Content = _rows;
        _body.SetColumns(GridLength.Fixed(220), GridLength.Star(), GridLength.Fixed(280));
        _body.SetRows(GridLength.Star());
        _body.Add(_hierarchy);
        _body.Add(_viewport);
        _body.Add(_scroll);
        _status.Add(new TextBlock { Text = "Ready", StyleKey = "Muted", AutomationName = "Diagnostics" });
        _root.Add(_toolbar);
        _root.Add(_body);
        _root.Add(_status);
        Content = _root;
    }
    public IUiElement Rows => _rows;
    public IInspectorItemSource? ItemSource
    {
        get => _source;
        set
        {
            if (_source is not null)
            {
                _source.Changed -= OnSourceChanged;
            }

            _source = value;
            if (_source is not null)
            {
                _source.Changed += OnSourceChanged;
            }

            Refresh(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Reset, 0, _source?.Count ?? 0));
        }
    }
    public void Refresh() => Refresh(new InspectorItemSourceChange(InspectorItemSourceChangeKind.Reset, 0, _source?.Count ?? 0));
    private void OnSourceChanged(object? sender, InspectorItemSourceChangeEventArgs args) => Refresh(args.Change);
    private void Refresh(InspectorItemSourceChange change)
    {
        if (_source is null) { _rows.ClearChildren(); _rowByKey.Clear(); _activeKeys.Clear(); return; }
        switch (change.Kind)
        {
            case InspectorItemSourceChangeKind.Reset:
                SyncAll();
                break;
            case InspectorItemSourceChangeKind.Add:
            case InspectorItemSourceChangeKind.Remove:
            case InspectorItemSourceChangeKind.Change:
                SyncRange(change.Index, change.Count);
                break;
        }
        CleanupRemovedFocus(change);
        Invalidate(UiDirtyFlags.Tree | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }
    private void SyncAll()
    {
        var source = RequireSource();
        _activeKeys.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            ApplyRecord(i);
        }

        Trim(source.Count);
    }
    private void SyncRange(int index, int count)
    {
        var source = RequireSource();
        if (index < 0)
        {
            index = 0;
        }

        if (count < 0)
        {
            count = 0;
        }

        switch (count)
        {
            case 0:
                break;
            default:
                for (var i = index; i < Math.Min(source.Count, index + count); i++)
                {
                    ApplyRecord(i);
                }

                if (index == 0 && _activeKeys.Count == 0)
                {
                    SyncAll();
                }

                break;
        }
        Trim(source.Count);
    }
    private void ApplyRecord(int index)
    {
        var record = RequireSource().GetRecord(index);
        var key = $"{record.ComponentKey}:{record.FieldKey}";
        if (!_rowByKey.TryGetValue(key, out var row))
        {
            row = new InspectorRow();
            _rowByKey[key] = row;
            _rows.Add(row);
        }
        row.Apply(record);
        if (index >= _activeKeys.Count)
        {
            _activeKeys.Add(key);
        }
        else
        {
            _activeKeys[index] = key;
        }
    }

    private IInspectorItemSource RequireSource() => _source ?? throw new InvalidOperationException("An inspector item source is required.");
    private void Trim(int count)
    {
        while (_activeKeys.Count > count)
        {
            var removed = _activeKeys[^1];
            _activeKeys.RemoveAt(_activeKeys.Count - 1);
            if (_rowByKey.Remove(removed, out var row))
            {
                _rows.Remove(row);
            }
        }
    }
    private void CleanupRemovedFocus(InspectorItemSourceChange change)
    {
        if (change.Kind != InspectorItemSourceChangeKind.Remove || change.Count == 0)
        {
            return;
        }

        foreach (var row in _rowByKey.Values)
        {
            if (row.ActiveEditor is UiElement editor && editor.IsFocused)
            {
                editor.SetFocused(false);
            }
        }
    }
}

public sealed class InspectorRow : Border
{
    private readonly StackPanel _grid = new() { Orientation = UiOrientation.Horizontal };
    private readonly TextBlock _label = new();
    private readonly TextBlock _value = new();
    private readonly TextBlock _diagnostic = new() { StyleKey = "Muted" };
    private readonly TextBox _editor = new() { Focusable = true };
    private readonly NumericEditor _numeric = new() { Focusable = true };
    public InspectorRow()
    {
        StyleKey = "InspectorRow";
        Padding = new UiThickness(6, 4, 6, 4);
        _grid.Add(_label);
        _grid.Add(_value);
        _grid.Add(_diagnostic);
        _grid.Add(_editor);
        _grid.Add(_numeric);
        Add(_grid);
    }
    public InspectorFieldRecord Record { get; private set; }
    public IUiElement ActiveEditor => Record.EditorKind.Equals("Numeric", StringComparison.OrdinalIgnoreCase) ? _numeric : _editor;
    public void Apply(InspectorFieldRecord record)
    {
        var previous = Record;
        Record = record;
        AutomationName = record.Label;
        AutomationRole = record.EditorKind;
        if (previous.Label != record.Label)
        {
            _label.Text = record.Label;
        }

        _value.Visibility = UiVisibility.Collapsed;
        _diagnostic.Text = record.IsReadOnly ? string.Empty : string.Empty;
        var numeric = record.EditorKind.Equals("Numeric", StringComparison.OrdinalIgnoreCase);
        if (numeric)
        {
            if (previous.ValueText != record.ValueText)
            {
                _numeric.Initialize(ParseNumeric(record.ValueText));
            }

            _numeric.Visibility = UiVisibility.Visible;
            _editor.Visibility = UiVisibility.Collapsed;
        }
        else
        {
            if (previous.ValueText != record.ValueText)
            {
                _editor.SetText(record.ValueText, false);
            }

            _editor.Visibility = UiVisibility.Visible;
            _numeric.Visibility = UiVisibility.Collapsed;
        }
    }
    private static double ParseNumeric(string text) => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
}
