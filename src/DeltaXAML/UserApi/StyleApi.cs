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

/// <summary>Small deterministic style value set; application uses the retained style source slot.</summary>
public sealed class UiStyle
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly UiResourceCatalog? _resources;

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

    public void Set(string propertyName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _values[propertyName] = value;
    }

    public void SetResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        _values[propertyName] = new UiResourceReference(resourceKey);
    }

    public void SetStaticResource(string propertyName, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (_resources is null)
        {
            throw new InvalidOperationException("A resource catalog is required for resource-backed style values.");
        }

        _values[propertyName] = new StaticResourceReference(resourceKey);
    }

    internal void Apply(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (TargetType != "*" && !string.Equals(TargetType, element.TypeName, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var pair in _values)
        {
            if (pair.Value is UiResourceReference reference)
            {
                if (_resources is null || !reference.IsValid)
                {
                    continue;
                }

                element.ApplyStyleResource(pair.Key, _resources, reference.Key);
            }
            else if (pair.Value is StaticResourceReference staticReference)
            {
                if (_resources is null || !staticReference.IsValid)
                {
                    continue;
                }

                if (_resources.TryResolve(staticReference.Key, out var value))
                {
                    element.ApplyStyleValue(pair.Key, value);
                }
            }
            else
            {
                element.ApplyStyleValue(pair.Key, pair.Value);
            }
        }
    }

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

    public UiTheme(UiResourceCatalog? resources = null)
    {
        Resources = resources ?? new UiResourceCatalog();
    }

    public UiResourceCatalog Resources { get; }

    public void Add(UiStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles.Add(style);
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
        ApplyRecursive(root);
    }

    private void ApplyRecursive(UiElement element)
    {
        if (element.StyleKey is { } styleKey)
        {
            foreach (var style in _styles)
            {
                if (string.Equals(style.Key, styleKey, StringComparison.Ordinal))
                {
                    style.Apply(element);
                }
            }
        }

        if (element.TemplateKey is { } templateKey && element.Children.Count == 0 && TryGetTemplate(templateKey, out var template))
        {
            element.SetTemplateContent(template.Build(element));
        }

        for (var i = 0; i < element.Children.Count; i++)
        {
            ApplyRecursive(element.Children[i]);
        }
    }
}
