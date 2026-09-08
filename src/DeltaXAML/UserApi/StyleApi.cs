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

        _store.Set(resource.Value, ToRetainedValue(value));
    }

    /// <summary>Registers an immutable typed effect resource by its existing identity.</summary>
    public void Set(UiEffectResource resource)
    {
        if (!resource.IsValid)
        {
            throw new ArgumentException("A typed effect resource must be internally consistent.", nameof(resource));
        }

        Set(resource.Set.Resource, resource);
    }

    /// <summary>Creates and registers a visual effect resource, returning its retained reference.</summary>
    public UiEffectSet RegisterEffects(UiResourceId resource, UiVisualEffects effects)
    {
        var value = UiEffects.Create(resource, effects);
        Set(value);
        return value.Set;
    }

    /// <summary>Creates and registers a text effect resource, returning its retained reference.</summary>
    public UiEffectSet RegisterEffects(UiResourceId resource, UiTextEffects effects)
    {
        var value = UiEffects.Create(resource, effects);
        Set(value);
        return value.Set;
    }

    /// <summary>Registers a visual effect resource under both a stable identity and a reusable XAML key.</summary>
    public UiEffectSet RegisterEffects(string key, UiResourceId resource, UiVisualEffects effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var value = UiEffects.Create(resource, effects);
        Set(value);
        Set(key, new UiResourceReference(resource));
        return value.Set;
    }

    /// <summary>Registers a text effect resource under both a stable identity and a reusable XAML key.</summary>
    public UiEffectSet RegisterEffects(string key, UiResourceId resource, UiTextEffects effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var value = UiEffects.Create(resource, effects);
        Set(value);
        Set(key, new UiResourceReference(resource));
        return value.Set;
    }

    /// <summary>Monotonic version changed only when a typed effect resource changes.</summary>
    public ulong EffectVersion => _store.EffectVersion;

    /// <summary>Resolves an immutable typed effect resource without an untyped cast at the caller.</summary>
    public bool TryResolveEffectResource(UiResourceId resource, out UiEffectResource value)
    {
        if (TryResolve(resource, out object? candidate) && candidate is UiEffectResource effect)
        {
            value = effect;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns a detached snapshot of typed effect resources for renderer setup.
    /// This is a cold operation; the returned values remain valid after catalog mutation.
    /// </summary>
    public UiEffectResource[] GetEffectResources() => _store.SnapshotEffectResources();

    public bool TryResolve(string key, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TryResolve(new Retained.UiResourceReference(key), out value);
    }

    public bool TryResolve(UiResourceId resource, out object? value)
    {
        if (!resource.IsValid)
        {
            value = null;
            return false;
        }

        return TryResolve(new Retained.UiResourceReference(resource.Value), out value);
    }

    private bool TryResolve(Retained.UiResourceReference reference, out object? value)
    {
        if (!_store.TryResolve(reference, out var retainedValue, out _))
        {
            value = null;
            return false;
        }

        value = ToPublicValue(retainedValue);
        return true;
    }

    private static object? ToRetainedValue(object? value) => value switch
    {
        UiColor color => new Retained.UiColor(color.R, color.G, color.B, color.A),
        UiThickness thickness => new Retained.UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        UiResourceReference reference => reference.Resource.IsValid
            ? new Retained.UiResourceReference(reference.Resource.Value)
            : new Retained.UiResourceReference(reference.Key),
        _ => value,
    };

    private static object? ToPublicValue(object? value) => value switch
    {
        Retained.UiColor color => new UiColor(color.R, color.G, color.B, color.A),
        Retained.UiThickness thickness => new UiThickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
        Retained.UiResourceReference reference => reference.HasResourceId
            ? new UiResourceReference(new UiResourceId(reference.ResourceId))
            : new UiResourceReference(reference.Key),
        _ => value,
    };
}

/// <summary>Explicit resource reference used by styles and resource markup.</summary>
public readonly record struct UiResourceReference(string Key)
{
    public UiResourceReference(UiResourceId resource) : this(resource.Value.ToString("D"))
    {
        if (!resource.IsValid)
        {
            throw new ArgumentException("A resource identity is required.", nameof(resource));
        }

        Resource = resource;
    }

    public UiResourceId Resource { get; } = UiResourceId.Empty;

    public bool IsValid => Resource.IsValid || !string.IsNullOrWhiteSpace(Key);
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
    private readonly Dictionary<string, StyleValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<UiStyleState, Dictionary<string, StyleValue>> _stateValues = new();
    private readonly UiResourceCatalog? _resources;
    private readonly UiTypeId _targetTypeId;
    private int _version;

    public UiStyle(string key, string targetType, UiResourceCatalog? resources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        Key = key;
        TargetType = targetType;
        _resources = resources;
    }

    /// <summary>Creates a compiled style selector backed by a stable element type identity.</summary>
    public UiStyle(string key, UiTypeId targetType, UiResourceCatalog? resources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!targetType.IsValid)
        {
            throw new ArgumentException("A stable target type identity is required.", nameof(targetType));
        }

        Key = key;
        TargetType = targetType.Value.ToString("D");
        _targetTypeId = targetType;
        _resources = resources;
    }

    public string Key { get; }

    public string TargetType { get; }

    internal int Version => _version;
    internal event EventHandler? Changed;

    public void Set(string propertyName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        SetValue(_values, new(propertyName, null, value));
    }

    /// <summary>Sets a style value through its typed property descriptor.</summary>
    public void Set<T>(UiProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        SetValue(_values, new(property.Name, property, value));
    }

    public void SetResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(_values, new(propertyName, null, new UiResourceReference(resourceKey)));
    }

    /// <summary>Sets a dynamic resource through its typed property descriptor.</summary>
    public void SetResource<T>(UiProperty<T> property, string resourceKey)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(_values, new(property.Name, property, new UiResourceReference(resourceKey)));
    }

    /// <summary>Sets a dynamic resource through its stable resource identity.</summary>
    public void SetResource<T>(UiProperty<T> property, UiResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(property);
        SetValue(_values, new(property.Name, property, new UiResourceReference(resource)));
    }

    public void SetStaticResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(_values, new(propertyName, null, new StaticResourceReference(resourceKey)));
    }

    /// <summary>Sets a static resource through its typed property descriptor.</summary>
    public void SetStaticResource<T>(UiProperty<T> property, string resourceKey)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(_values, new(property.Name, property, new StaticResourceReference(resourceKey)));
    }

    /// <summary>Sets a static resource through its stable resource identity.</summary>
    public void SetStaticResource<T>(UiProperty<T> property, UiResourceId resource)
    {
        ArgumentNullException.ThrowIfNull(property);
        SetValue(_values, new(property.Name, property, new StaticResourceReference(resource)));
    }

    public void SetState(UiStyleState state, string propertyName, object? value)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        SetValue(GetStateValues(state), new(propertyName, null, value));
    }

    /// <summary>Sets a visual-state value through its typed property descriptor.</summary>
    public void SetState<T>(UiStyleState state, UiProperty<T> property, T value)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(property);
        SetValue(GetStateValues(state), new(property.Name, property, value));
    }

    public void SetStateResource(UiStyleState state, string propertyName, string resourceKey)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(GetStateValues(state), new(propertyName, null, new UiResourceReference(resourceKey)));
    }

    /// <summary>Sets a dynamic visual-state resource through its typed property descriptor.</summary>
    public void SetStateResource<T>(UiStyleState state, UiProperty<T> property, string resourceKey)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(GetStateValues(state), new(property.Name, property, new UiResourceReference(resourceKey)));
    }

    /// <summary>Sets a dynamic visual-state resource through its stable resource identity.</summary>
    public void SetStateResource<T>(UiStyleState state, UiProperty<T> property, UiResourceId resource)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(property);
        SetValue(GetStateValues(state), new(property.Name, property, new UiResourceReference(resource)));
    }

    public void SetStateStaticResource(UiStyleState state, string propertyName, string resourceKey)
    {
        ValidateState(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(GetStateValues(state), new(propertyName, null, new StaticResourceReference(resourceKey)));
    }

    /// <summary>Sets a static visual-state resource through its typed property descriptor.</summary>
    public void SetStateStaticResource<T>(UiStyleState state, UiProperty<T> property, string resourceKey)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        RequireResources();
        SetValue(GetStateValues(state), new(property.Name, property, new StaticResourceReference(resourceKey)));
    }

    /// <summary>Sets a static visual-state resource through its stable resource identity.</summary>
    public void SetStateStaticResource<T>(UiStyleState state, UiProperty<T> property, UiResourceId resource)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(property);
        SetValue(GetStateValues(state), new(property.Name, property, new StaticResourceReference(resource)));
    }

    internal void Apply(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if ((_targetTypeId.IsValid && !Retained.UiDescriptorCatalog.MatchesType(element.RetainedElement, _targetTypeId)) ||
            (!_targetTypeId.IsValid && TargetType != "*" && !string.Equals(TargetType, element.TypeName, StringComparison.Ordinal)))
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
                    ApplyValue(element, baseValue);
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

    private void ApplyValues(UiElement element, Dictionary<string, StyleValue> values)
    {
        foreach (var pair in values)
        {
            ApplyValue(element, pair.Value);
        }
    }

    private void ApplyValue(UiElement element, StyleValue styleValue)
    {
        var propertyName = styleValue.Name;
        var value = styleValue.Value;
        if (value is UiResourceReference reference)
        {
            if (_resources is not null && reference.IsValid)
            {
                if (reference.Resource.IsValid)
                {
                    element.ApplyStyleResource(propertyName, _resources, reference.Resource);
                }
                else
                {
                    element.ApplyStyleResource(propertyName, _resources, reference.Key);
                }
            }
        }
        else if (value is StaticResourceReference staticReference)
        {
            if (_resources is not null && staticReference.IsValid &&
                (staticReference.Resource.IsValid
                    ? _resources.TryResolve(staticReference.Resource, out var resolved)
                    : _resources.TryResolve(staticReference.Key, out resolved)))
            {
                element.ApplyStyleValue(propertyName, resolved);
            }
        }
        else
        {
            if (styleValue.Property is { } property)
            {
                element.ApplyStyleValue(property, value);
            }
            else
            {
                element.ApplyStyleValue(propertyName, value);
            }
        }
    }

    internal void ClearApplied(UiElement element)
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

        element.RetainedElement.ClearAppliedStyle();
    }

    private void SetValue(Dictionary<string, StyleValue> values, StyleValue value)
    {
        if (values.TryGetValue(value.Name, out var current) && current.Equals(value))
        {
            return;
        }

        values[value.Name] = value;
        _version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RequireResources()
    {
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }
    }

    private Dictionary<string, StyleValue> GetStateValues(UiStyleState state) =>
        _stateValues.TryGetValue(state, out var values) ? values : AddStateValues(state);

    private Dictionary<string, StyleValue> AddStateValues(UiStyleState state)
    {
        var values = new Dictionary<string, StyleValue>(StringComparer.Ordinal);
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

    private readonly record struct StyleValue(string Name, IUiProperty? Property, object? Value);

    private readonly record struct StaticResourceReference(string Key)
    {
        internal StaticResourceReference(UiResourceId resource) : this(resource.Value.ToString("D"))
        {
            if (!resource.IsValid)
            {
                throw new ArgumentException("A resource identity is required.", nameof(resource));
            }

            Resource = resource;
        }

        internal UiResourceId Resource { get; } = UiResourceId.Empty;
        internal bool IsValid => !string.IsNullOrWhiteSpace(Key);
    }
}

/// <summary>Creates a retained template subtree without owning a document or renderer state.</summary>
public interface IUiTemplateFactory
{
    UiElement Create(UiElement owner, UiResourceCatalog resources);
}

/// <summary>Reusable code template. It creates a retained subtree only when applied.</summary>
public sealed class UiTemplate
{
    private readonly IUiTemplateFactory _factory;

    public UiTemplate(IUiTemplateFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    internal UiElement Build(UiElement owner, UiResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resources);
        return _factory.Create(owner, resources);
    }
}

/// <summary>Style/resource/template collection applied to an existing retained tree.</summary>
public sealed class UiTheme
{
    private readonly List<UiStyle> _styles = new();
    private readonly Dictionary<UiStyleId, UiStyle> _compiledStyles = new();
    private readonly Dictionary<string, UiTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<UiTemplateId, UiTemplate> _compiledTemplates = new();
    private readonly List<UiElement> _stateTraversal = new();
    private readonly Dictionary<string, List<UiElement>> _styleDependents = new(StringComparer.Ordinal);
    private readonly Dictionary<UiStyle, List<UiElement>> _compiledStyleDependents = new();
    private readonly List<UiStyle> _changedStyles = new();
    private uint _templateGeneration;
    private uint _appliedTemplateGeneration;

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
        QueueStyleRefresh(style);
    }

    /// <summary>Registers a generated style under its stable compiled identity.</summary>
    public void RegisterStyle(UiStyleId id, UiStyle style)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable style identity is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(style);
        if (!_compiledStyles.TryAdd(id, style))
        {
            throw new ArgumentException($"The compiled style identity '{id.Value}' is registered twice.", nameof(id));
        }

        _styles.Add(style);
        style.Changed += OnStyleChanged;
        QueueStyleRefresh(style);
    }

    public void RegisterTemplate(string key, UiTemplate template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(template);
        _templates[key] = template;
        _templateGeneration++;
    }

    /// <summary>Registers a generated template under its stable identity.</summary>
    public void RegisterTemplate(UiTemplateId id, UiTemplate template)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable template identity is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(template);
        _compiledTemplates[id] = template;
        _templateGeneration++;
    }

    public bool TryGetTemplate(string key, [NotNullWhen(true)] out UiTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _templates.TryGetValue(key, out template);
    }

    public bool TryGetTemplate(UiTemplateId id, [NotNullWhen(true)] out UiTemplate? template)
    {
        if (!id.IsValid)
        {
            template = null;
            return false;
        }

        return _compiledTemplates.TryGetValue(id, out template);
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
            TrackStyleDependency(element);
            FindStyle(element)?.Apply(element);

            ApplyTemplate(element);

            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                _stateTraversal.Add(element.Children[i]);
            }
        }
    }

    internal void RefreshStates(
        Retained.UiNodeStore nodes,
        UiElement root,
        List<Retained.UiNodeId> traversal,
        List<Retained.UiNodeId> childOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(childOrder);
        LastRefreshCount = 0;
        var fullRefresh = _templateGeneration != _appliedTemplateGeneration;
        for (var styleIndex = 0; styleIndex < _changedStyles.Count; styleIndex++)
        {
            var style = _changedStyles[styleIndex];
            if (_compiledStyleDependents.TryGetValue(style, out var compiledDependents))
            {
                InvalidateDependents(compiledDependents);
            }

            if (_styleDependents.TryGetValue(style.Key, out var dependents))
            {
                for (var dependentIndex = 0; dependentIndex < dependents.Count; dependentIndex++)
                {
                    var dependent = dependents[dependentIndex];
                    if (string.Equals(dependent.StyleKey, style.Key, StringComparison.Ordinal))
                    {
                        dependent.RetainedElement.InvalidateChanged(Retained.UiDirtyMask.Style);
                    }
                }
            }
        }

        if (!fullRefresh && _changedStyles.Count == 0 && !root.RetainedElement.IsStyleDirty)
        {
            return;
        }

        traversal.Clear();
        traversal.Add(new(root.RetainedElement.Id.Value, root.RetainedElement.Generation));
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var id = traversal[last];
            traversal.RemoveAt(last);
            if (!nodes.TryGetNode(id, out var node) || node.Element is not { } retained)
            {
                continue;
            }

            var element = root.WrapRetained(retained);
            TrackStyleDependency(element);
            if (!fullRefresh && !element.RetainedElement.IsStyleDirty)
            {
                continue;
            }

            LastRefreshCount++;
            var applied = false;
            if (FindStyle(element) is { } style)
            {
                style.ApplyState(element, UiStyle.CurrentState(element));
                applied = true;
            }

            if (!applied && element.RetainedElement.AppliedStyle is { } previous)
            {
                previous.ClearApplied(element);
            }

            ApplyTemplate(element);

            element.RetainedElement.CompleteStyleStage();
            if (!nodes.TryCopyLogicalChildren(node.Id, childOrder))
            {
                continue;
            }

            for (var i = childOrder.Count - 1; i >= 0; i--)
            {
                if (fullRefresh || (nodes.TryGetNode(childOrder[i], out var childNode) &&
                    childNode.Element is { } child && child.IsStyleDirty))
                {
                    traversal.Add(childOrder[i]);
                }
            }
        }

        _changedStyles.Clear();
        _appliedTemplateGeneration = _templateGeneration;
    }

    private void OnStyleChanged(object? sender, EventArgs args)
    {
        if (sender is UiStyle style)
        {
            QueueStyleRefresh(style);
        }
    }

    private void QueueStyleRefresh(UiStyle style)
    {
        for (var i = 0; i < _changedStyles.Count; i++)
        {
            if (ReferenceEquals(_changedStyles[i], style))
            {
                return;
            }
        }

        _changedStyles.Add(style);
    }

    private void TrackStyleDependency(UiElement element)
    {
        if (element.RetainedElement.CompiledStyleId != Guid.Empty && FindStyle(element) is { } compiledStyle)
        {
            if (!_compiledStyleDependents.TryGetValue(compiledStyle, out var compiledDependents))
            {
                compiledDependents = new List<UiElement>();
                _compiledStyleDependents.Add(compiledStyle, compiledDependents);
            }

            AddDependent(compiledDependents, element);
            return;
        }

        if (element.StyleKey is not { } key)
        {
            return;
        }

        if (!_styleDependents.TryGetValue(key, out var dependents))
        {
            dependents = new List<UiElement>();
            _styleDependents.Add(key, dependents);
        }

        AddDependent(dependents, element);
    }

    private UiStyle? FindStyle(UiElement element)
    {
        var compiledStyleId = element.RetainedElement.CompiledStyleId;
        if (compiledStyleId != Guid.Empty && _compiledStyles.TryGetValue(new(compiledStyleId), out var compiledStyle))
        {
            return compiledStyle;
        }

        if (element.StyleKey is not { } key)
        {
            return null;
        }

        for (var i = 0; i < _styles.Count; i++)
        {
            if (string.Equals(_styles[i].Key, key, StringComparison.Ordinal))
            {
                return _styles[i];
            }
        }

        return null;
    }

    private static void AddDependent(List<UiElement> dependents, UiElement element)
    {
        for (var i = 0; i < dependents.Count; i++)
        {
            if (ReferenceEquals(dependents[i], element))
            {
                return;
            }
        }

        dependents.Add(element);
    }

    private static void InvalidateDependents(List<UiElement> dependents)
    {
        for (var i = 0; i < dependents.Count; i++)
        {
            dependents[i].RetainedElement.InvalidateChanged(Retained.UiDirtyMask.Style);
        }
    }

    private void ApplyTemplate(UiElement element)
    {
        UiTemplate? template = null;
        if (element.RetainedElement.CompiledTemplateId.IsValid)
        {
            _compiledTemplates.TryGetValue(element.RetainedElement.CompiledTemplateId, out template);
        }
        else if (element.TemplateKey is { } templateKey)
        {
            _templates.TryGetValue(templateKey, out template);
        }

        if (template is null)
        {
            if (element.HasTemplateContent)
            {
                element.ClearTemplateContent();
            }

            return;
        }

        if (element.HasTemplateContent && !ReferenceEquals(element.AppliedTemplate, template))
        {
            element.ClearTemplateContent();
        }

        if (!element.HasTemplateContent && element.Children.Count == 0)
        {
            element.SetTemplateContent(template.Build(element, Resources), template);
        }
    }
}
