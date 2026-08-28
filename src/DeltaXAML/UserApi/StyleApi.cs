using System.Diagnostics.CodeAnalysis;
using Delta.XAML.Contract;
using Retained = DeltaXAML.Internal;

namespace Delta.XAML;

/// <summary>Optional name-based resource boundary used by DynamicResource markup.</summary>
public interface IUiNamedResourceResolver
{
    bool TryResolve(string key, out object? value);
}

/// <summary>Mutable resource catalog with stable names and GUID aliases.</summary>
public sealed class UiResourceCatalog : IUiResourceResolver, IUiNamedResourceResolver
{
    private readonly Retained.UiResourceStore _store = new();

    internal Retained.UiResourceStore Store => _store;

    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _store.Set(key, ToRetainedValue(value));
    }

    public void Set(UiResourceId resource, object? value)
    {
        if (!resource.IsValid)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }

        Set(resource.Value.ToString("D"), value);
    }

    public bool TryResolve(string key, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_store.TryResolve(key, out var retainedValue, out _))
        {
            value = null;
            return false;
        }

        value = ToPublicValue(retainedValue);
        return true;
    }

    public bool TryResolve(UiResourceId resource, out object? value)
    {
        if (!resource.IsValid)
        {
            value = null;
            return false;
        }

        return TryResolve(resource.Value.ToString("D"), out value);
    }

    private static object? ToRetainedValue(object? value) => value switch
    {
        UiColor color => new Retained.UiColor(color.R, color.G, color.B, color.A),
        UiThickness thickness => new Retained.UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        UiResourceReference reference => new Retained.UiResourceReference(reference.Key),
        _ => value,
    };

    private static object? ToPublicValue(object? value) => value switch
    {
        Retained.UiColor color => new UiColor(color.R, color.G, color.B, color.A),
        Retained.UiThickness thickness => new UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        Retained.UiResourceReference reference => new UiResourceReference(reference.Key),
        _ => value,
    };
}

/// <summary>Explicit resource reference used by styles and resource markup.</summary>
public readonly record struct UiResourceReference(string Key)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Key);
}

/// <summary>Closed visual-state vocabulary accepted by compiled styles.</summary>
public enum UiStyleState
{
    None,
    Unknown,
    Normal,
    Hover,
    Pressed,
    Focused,
    Disabled,
    Invalid,
    Selected,
}

/// <summary>Small deterministic style value set; application uses the retained style source slot.</summary>
public sealed class UiStyle
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<UiStyleState, Dictionary<string, object?>> _stateValues = new();
    private readonly UiResourceCatalog? _resources;
    private int _version;

    public UiStyle(string key, string targetType, UiResourceCatalog? resources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        Key = key;
        TargetType = targetType;
        _resources = resources;
    }

    public string Key { get; }

    public string TargetType { get; }

    internal int Version => _version;
    internal event EventHandler? Changed;

    public void Set(string propertyName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        SetValue(_values, propertyName, value);
    }

    public void SetResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        SetValue(_values, propertyName, new UiResourceReference(resourceKey));
    }

    public void SetStaticResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        SetValue(_values, propertyName, new StaticResourceReference(resourceKey));
    }

    public void SetState(UiStyleState state, string propertyName, object? value)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        SetValue(GetStateValues(state), propertyName, value);
    }

    public void SetStateResource(UiStyleState state, string propertyName, string resourceKey)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        SetValue(GetStateValues(state), propertyName, new UiResourceReference(resourceKey));
    }

    public void SetStateStaticResource(UiStyleState state, string propertyName, string resourceKey)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        SetValue(GetStateValues(state), propertyName, new StaticResourceReference(resourceKey));
    }

    internal void Apply(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (TargetType != "*" && !string.Equals(TargetType, element.TypeName, StringComparison.Ordinal))
        {
            return;
        }

        var state = CurrentState(element);
        if (ReferenceEquals(element.RetainedElement.AppliedStyle, this) &&
            element.RetainedElement.AppliedStyleVersion == Version)
        {
            ApplyState(element, state);
            return;
        }

        if (element.RetainedElement.AppliedStyle is { } previous)
        {
            previous.ClearApplied(element);
        }

        ApplyValues(element, _values);
        ApplyStateValues(element, state);
        element.RetainedElement.SetAppliedStyle(this, state, Version);
    }

    internal void ApplyState(UiElement element, UiStyleState state)
    {
        ArgumentNullException.ThrowIfNull(element);
        ValidateState(state);
        if (!ReferenceEquals(element.RetainedElement.AppliedStyle, this) ||
            element.RetainedElement.AppliedStyleVersion != Version)
        {
            Apply(element);
            return;
        }

        var previousState = element.RetainedElement.AppliedStyleState;
        if (previousState == state)
        {
            return;
        }

        _stateValues.TryGetValue(state, out var nextValues);
        if (_stateValues.TryGetValue(previousState, out var previousValues))
        {
            foreach (var pair in previousValues)
            {
                if (nextValues is not null && nextValues.ContainsKey(pair.Key))
                {
                    continue;
                }

                if (_values.TryGetValue(pair.Key, out var baseValue))
                {
                    ApplyValue(element, pair.Key, baseValue);
                }
                else
                {
                    element.RetainedElement.ClearStyleValue(pair.Key);
                }
            }
        }

        ApplyStateValues(element, state);
        element.RetainedElement.SetAppliedStyle(this, state, Version);
    }

    private void ApplyStateValues(UiElement element, UiStyleState state)
    {
        if (_stateValues.TryGetValue(state, out var values))
        {
            ApplyValues(element, values);
        }
    }

    private void ApplyValues(UiElement element, Dictionary<string, object?> values)
    {
        foreach (var pair in values)
        {
            ApplyValue(element, pair.Key, pair.Value);
        }
    }

    private void ApplyValue(UiElement element, string propertyName, object? value)
    {
        if (value is UiResourceReference reference)
        {
            if (_resources is not null && reference.IsValid)
            {
                element.ApplyStyleResource(propertyName, _resources, reference.Key);
            }
        }
        else if (value is StaticResourceReference staticReference)
        {
            if (_resources is not null && staticReference.IsValid && _resources.TryResolve(staticReference.Key, out var resolved))
            {
                element.ApplyStyleValue(propertyName, resolved);
            }
        }
        else
        {
            element.ApplyStyleValue(propertyName, value);
        }
    }

    private void ClearApplied(UiElement element)
    {
        foreach (var property in _values.Keys)
        {
            element.RetainedElement.ClearStyleValue(property);
        }

        foreach (var values in _stateValues.Values)
        {
            foreach (var property in values.Keys)
            {
                if (!_values.ContainsKey(property))
                {
                    element.RetainedElement.ClearStyleValue(property);
                }
            }
        }
    }

    private void SetValue(Dictionary<string, object?> values, string propertyName, object? value)
    {
        if (values.TryGetValue(propertyName, out var current) && Equals(current, value))
        {
            return;
        }

        values[propertyName] = value;
        _version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, object?> GetStateValues(UiStyleState state) =>
        _stateValues.TryGetValue(state, out var values) ? values : AddStateValues(state);

    private Dictionary<string, object?> AddStateValues(UiStyleState state)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        _stateValues.Add(state, values);
        return values;
    }

    private static void ValidateState(UiStyleState state)
    {
        if (state is UiStyleState.None or UiStyleState.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A concrete visual state is required.");
        }
    }

    internal static UiStyleState CurrentState(UiElement element) => element.RetainedElement.VisualState.State switch
    {
        Retained.UiVisualState.Normal => UiStyleState.Normal,
        Retained.UiVisualState.Hover => UiStyleState.Hover,
        Retained.UiVisualState.Pressed => UiStyleState.Pressed,
        Retained.UiVisualState.Focused => UiStyleState.Focused,
        Retained.UiVisualState.Disabled => UiStyleState.Disabled,
        Retained.UiVisualState.Invalid => UiStyleState.Invalid,
        Retained.UiVisualState.Selected => UiStyleState.Selected,
        _ => UiStyleState.Unknown,
    };

    private readonly record struct StaticResourceReference(string Key)
    {
        internal bool IsValid => !string.IsNullOrWhiteSpace(Key);
    }
}

/// <summary>Reusable code template. It creates a retained subtree only when applied.</summary>
public sealed class UiTemplate
{
    private readonly Func<UiElement, UiElement> _build;

    public UiTemplate(Func<UiElement, UiElement> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    internal UiElement Build(UiElement owner) => _build(owner);
}

/// <summary>Style/resource/template collection applied to an existing retained tree.</summary>
public sealed class UiTheme
{
    private readonly List<UiStyle> _styles = new();
    private readonly Dictionary<string, UiTemplate> _templates = new(StringComparer.Ordinal);
    private readonly List<UiElement> _stateTraversal = new();
    private uint _styleGeneration;
    private uint _appliedStyleGeneration;

    internal int LastRefreshCount { get; private set; }

    public UiTheme(UiResourceCatalog? resources = null)
    {
        Resources = resources ?? new UiResourceCatalog();
    }

    public UiResourceCatalog Resources { get; }

    public void Add(UiStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles.Add(style);
        style.Changed += OnStyleChanged;
        _styleGeneration++;
    }

    public void RegisterTemplate(string key, UiTemplate template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(template);
        _templates[key] = template;
    }

    public bool TryGetTemplate(string key, [NotNullWhen(true)] out UiTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _templates.TryGetValue(key, out template);
    }

    public void Apply(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _stateTraversal.Clear();
        _stateTraversal.Add(root);
        while (_stateTraversal.Count != 0)
        {
            var last = _stateTraversal.Count - 1;
            var element = _stateTraversal[last];
            _stateTraversal.RemoveAt(last);
            if (element.StyleKey is { } styleKey)
            {
                for (var i = 0; i < _styles.Count; i++)
                {
                    if (string.Equals(_styles[i].Key, styleKey, StringComparison.Ordinal))
                    {
                        _styles[i].Apply(element);
                    }
                }
            }

            if (element.TemplateKey is { } templateKey && element.Children.Count == 0 && TryGetTemplate(templateKey, out var template))
            {
                element.SetTemplateContent(template.Build(element));
            }

            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                _stateTraversal.Add(element.Children[i]);
            }
        }
    }

    internal void RefreshStates(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        LastRefreshCount = 0;
        var fullRefresh = _styleGeneration != _appliedStyleGeneration;
        if (!fullRefresh && !root.RetainedElement.IsStyleDirty)
        {
            return;
        }

        _stateTraversal.Clear();
        _stateTraversal.Add(root);
        while (_stateTraversal.Count != 0)
        {
            var last = _stateTraversal.Count - 1;
            var element = _stateTraversal[last];
            _stateTraversal.RemoveAt(last);
            if (!fullRefresh && !element.RetainedElement.IsStyleDirty)
            {
                continue;
            }

            LastRefreshCount++;
            if (element.StyleKey is { } styleKey)
            {
                for (var i = 0; i < _styles.Count; i++)
                {
                    if (string.Equals(_styles[i].Key, styleKey, StringComparison.Ordinal))
                    {
                        _styles[i].ApplyState(element, UiStyle.CurrentState(element));
                    }
                }
            }

            element.RetainedElement.CompleteStyleStage();
            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                var child = element.Children[i];
                if (fullRefresh || child.RetainedElement.IsStyleDirty)
                {
                    _stateTraversal.Add(child);
                }
            }
        }

        _appliedStyleGeneration = _styleGeneration;
    }

    private void OnStyleChanged(object? sender, EventArgs args) => _styleGeneration++;
}
