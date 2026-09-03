using Delta;

namespace DeltaXAML.Internal;

internal sealed class UiAccessibilityStage
{
    private Delta.XAML.UiSemanticNode[] _nodes = Array.Empty<Delta.XAML.UiSemanticNode>();
    private readonly List<UiElement> _traversal = new();
    private int _count;
    private uint _version;
    private uint _sourceVersion;

    internal Delta.XAML.UiSemanticSnapshot Extract(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_sourceVersion == root.OutputVersion)
        {
            return new(_nodes.AsSpan(0, _count), _version);
        }

        _count = 0;
        _traversal.Clear();
        _traversal.Add(root);
        while (_traversal.Count != 0)
        {
            var last = _traversal.Count - 1;
            var element = _traversal[last];
            _traversal.RemoveAt(last);
            if (element.Visibility != UiVisibility.Visible || (element.Participation & Delta.XAML.UiParticipation.Layout) == 0)
            {
                continue;
            }

            EnsureCapacity(_count + 1);
            var metadata = element.Automation;
            var position = element.Parent is { } parent ? FindChild(parent, element) + 1 : 1;
            var setSize = element.Parent?.Children.Count ?? 1;
            _nodes[_count++] = new(
                element.Id.Value,
                element.Generation,
                ToPublicRole(metadata.Role),
                metadata.Name,
                metadata.ValueText,
                new float4(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height),
                Actions(element),
                metadata.IsEnabled,
                element.IsFocused,
                metadata.IsInvalid,
                position,
                setSize);
            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                _traversal.Add((UiElement)element.Children[i]);
            }
        }

        _sourceVersion = root.OutputVersion;
        _version++;
        return new(_nodes.AsSpan(0, _count), _version);
    }

    private static int FindChild(UiElement parent, UiElement child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return 0;
    }

    private static Delta.XAML.UiSemanticActions Actions(UiElement element)
    {
        var actions = element.Focusable ? Delta.XAML.UiSemanticActions.Focus : Delta.XAML.UiSemanticActions.None;
        return element switch
        {
            Button or ToggleButton => actions | Delta.XAML.UiSemanticActions.Invoke,
            TextBox => actions | Delta.XAML.UiSemanticActions.SetValue,
            NumericEditor or Slider => actions | Delta.XAML.UiSemanticActions.SetValue |
                Delta.XAML.UiSemanticActions.Increment | Delta.XAML.UiSemanticActions.Decrement,
            ScrollViewer => actions | Delta.XAML.UiSemanticActions.Scroll,
            _ when element.Automation.Role == UiAutomationRole.ListItem => actions | Delta.XAML.UiSemanticActions.Select,
            _ when element.Command.IsValid => actions | Delta.XAML.UiSemanticActions.Invoke,
            _ => actions,
        };
    }

    private static Delta.XAML.UiSemanticRole ToPublicRole(UiAutomationRole role) => role switch
    {
        UiAutomationRole.None => Delta.XAML.UiSemanticRole.None,
        UiAutomationRole.Unknown => Delta.XAML.UiSemanticRole.Unknown,
        UiAutomationRole.Generic => Delta.XAML.UiSemanticRole.Generic,
        UiAutomationRole.Button => Delta.XAML.UiSemanticRole.Button,
        UiAutomationRole.Window => Delta.XAML.UiSemanticRole.Window,
        UiAutomationRole.Text => Delta.XAML.UiSemanticRole.Text,
        UiAutomationRole.TextBox => Delta.XAML.UiSemanticRole.TextBox,
        UiAutomationRole.NumericEditor => Delta.XAML.UiSemanticRole.NumericEditor,
        UiAutomationRole.Slider => Delta.XAML.UiSemanticRole.Slider,
        UiAutomationRole.Image => Delta.XAML.UiSemanticRole.Image,
        UiAutomationRole.List => Delta.XAML.UiSemanticRole.List,
        UiAutomationRole.ListItem => Delta.XAML.UiSemanticRole.ListItem,
        UiAutomationRole.Menu => Delta.XAML.UiSemanticRole.Menu,
        UiAutomationRole.Tab => Delta.XAML.UiSemanticRole.Tab,
        UiAutomationRole.Link => Delta.XAML.UiSemanticRole.Link,
        _ => Delta.XAML.UiSemanticRole.Unknown,
    };

    private void EnsureCapacity(int count)
    {
        if (_nodes.Length < count)
        {
            Array.Resize(ref _nodes, Maths.Max(8, Maths.Max(count, _nodes.Length * 2)));
        }
    }
}
