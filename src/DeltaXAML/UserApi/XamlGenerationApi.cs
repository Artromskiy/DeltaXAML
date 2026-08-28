namespace Delta.XAML;

/// <summary>Declares how an attributed custom XAML type accepts nested content.</summary>
public enum UiXamlContentKind : byte
{
    None,
    Children,
    SingleContent,
}

/// <summary>Closed literal vocabulary understood by the DeltaXAML compiler.</summary>
public enum UiXamlValueKind : byte
{
    Text,
    Boolean,
    Real32,
    Real64,
    Color,
    Thickness,
    GridLengthList,
    Enum,
}

/// <summary>Registers a custom element with the compile-time XAML generator.</summary>
/// <remarks>
/// The generator reads this metadata from source symbols. Runtime loading still
/// uses <see cref="IXamlTypeResolver"/> explicitly and performs no assembly scan.
/// Children content requires an <c>Add(UiElement)</c> method; single content
/// requires <c>SetContent(UiElement)</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UiXamlTypeAttribute : Attribute
{
    public UiXamlTypeAttribute(
        string xmlNamespace,
        string name,
        string stableId,
        UiXamlContentKind content = UiXamlContentKind.None)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        XmlNamespace = xmlNamespace;
        Name = name;
        StableId = stableId;
        Content = content;
    }

    public string XmlNamespace { get; }

    public string Name { get; }

    public string StableId { get; }

    public UiXamlContentKind Content { get; }
}

/// <summary>Registers a direct settable property of an attributed custom XAML type.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiXamlPropertyAttribute : Attribute
{
    public UiXamlPropertyAttribute(string stableId, UiXamlValueKind valueKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        StableId = stableId;
        ValueKind = valueKind;
    }

    public string StableId { get; }

    public UiXamlValueKind ValueKind { get; }
}

/// <summary>Direction of one compile-time converter method.</summary>
public enum UiXamlConverterDirection : byte
{
    Forward,
    Backward,
}

/// <summary>Registers a static typed converter method for generated bindings.</summary>
/// <remarks>
/// A forward method accepts the source value and returns the target value. A
/// backward method with the same key accepts the target value and returns the
/// source value. Generated code calls both methods directly.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UiXamlConverterAttribute : Attribute
{
    public UiXamlConverterAttribute(
        string key,
        UiXamlConverterDirection direction = UiXamlConverterDirection.Forward)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Direction = direction;
    }

    public string Key { get; }

    public UiXamlConverterDirection Direction { get; }
}
