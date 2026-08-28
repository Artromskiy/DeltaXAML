namespace Delta.XAML;

/// <summary>Typed property descriptor used by the low-boilerplate user API.</summary>
public sealed class UiProperty<T> : IUiProperty<T>
{
    public UiProperty(UiPropertyId id, string name, T defaultValue)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("A stable property identity is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        DefaultValue = defaultValue;
    }

    public UiPropertyId Id { get; }

    public string Name { get; }

    public Type ValueType => typeof(T);

    public T DefaultValue { get; }

    object? IUiProperty.DefaultValue => DefaultValue;
}

/// <summary>Common typed properties available on every element.</summary>
public static class UiElementProperties
{
    public static UiProperty<float> Width { get; } = Create<float>("10000000-0000-4000-8000-000000000001", "Width", float.NaN);
    public static UiProperty<float> Height { get; } = Create<float>("10000000-0000-4000-8000-000000000002", "Height", float.NaN);
    public static UiProperty<UiColor> Background { get; } = Create<UiColor>("10000000-0000-4000-8000-000000000003", "Background", default);
    public static UiProperty<UiThickness> Padding { get; } = Create("10000000-0000-4000-8000-000000000004", "Padding", UiThickness.Zero);
    public static UiProperty<bool> Fill { get; } = Create("10000000-0000-4000-8000-000000000005", "Fill", false);
    public static UiProperty<bool> IsEnabled { get; } = Create("10000000-0000-4000-8000-000000000006", "IsEnabled", true);
    public static UiProperty<bool> IsSelected { get; } = Create("10000000-0000-4000-8000-000000000007", "IsSelected", false);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed text properties for <see cref="UiTextBlock"/> and text editors.</summary>
public static class UiTextBlockProperties
{
    public static UiProperty<string> Text { get; } = Create("20000000-0000-4000-8000-000000000001", "Text", string.Empty);
    public static UiProperty<string> FontKey { get; } = Create("20000000-0000-4000-8000-000000000002", "FontKey", "default");
    public static UiProperty<float> FontSize { get; } = Create("20000000-0000-4000-8000-000000000003", "FontSize", 14f);
    public static UiProperty<UiColor> Foreground { get; } = Create("20000000-0000-4000-8000-000000000004", "Foreground", new UiColor(255, 255, 255));

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}

/// <summary>Typed numeric properties for <see cref="UiNumericEditor"/>.</summary>
public static class UiNumericEditorProperties
{
    public static UiProperty<double> Value { get; } = Create("30000000-0000-4000-8000-000000000001", "Value", 0d);
    public static UiProperty<double> Minimum { get; } = Create("30000000-0000-4000-8000-000000000002", "Minimum", double.MinValue);
    public static UiProperty<double> Maximum { get; } = Create("30000000-0000-4000-8000-000000000003", "Maximum", double.MaxValue);

    private static UiProperty<T> Create<T>(string id, string name, T defaultValue) =>
        new(new UiPropertyId(Guid.Parse(id)), name, defaultValue);
}
