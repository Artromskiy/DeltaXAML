using System.Diagnostics.CodeAnalysis;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal partial class UiElement
{
    public void SetDefault(string name, object? value, UiDirtyFlags invalidation) => _properties.SetDefault(name, value, invalidation); public void SetLocal(string name, object? value, UiDirtyFlags invalidation) => _properties.SetLocal(name, value, invalidation); public void SetStyle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetStyle(name, value, invalidation); public void SetTrigger(string name, object? value, UiDirtyFlags invalidation) => _properties.SetTrigger(name, value, invalidation); public void SetBinding(string name, UiBindingValue binding, UiDirtyFlags invalidation) => _properties.SetBinding(name, binding, invalidation); public void SetHandle(string name, object? value, UiDirtyFlags invalidation) => _properties.SetHandle(name, value, invalidation); public void SetAnimation(string name, object? value, UiDirtyFlags invalidation) => _properties.SetAnimation(name, value, invalidation); public void SetStyleResource(string name, UiResourceStore resources, UiResourceReference reference, UiDirtyFlags invalidation) => _properties.SetStyleResource(name, resources, reference, invalidation); public void Clear(string name, UiValueSource source) => _properties.Clear(name, source); public bool TryGet(string name, out UiValue value) => _properties.TryGet(name, out value);
    internal bool TryGetResourceDiagnostic(
        [NotNullWhen(true)] out string? code,
        [NotNullWhen(true)] out string? message) => _properties.TryGetResourceDiagnostic(out code, out message);
    public UiPropertyHandle GetHandle(string name) => _properties.GetHandle(name);
    public bool TrySet(UiPropertyHandle handle, object? value, UiDirtyFlags invalidation, [NotNullWhen(false)] out string? diagnostic) => _properties.TrySet(handle, value, invalidation, out diagnostic);
    private string GetAutomationValueText() => UiDescriptorCatalog.GetAutomationValueText(this);

    internal Type BindingTargetType(string propertyName) => UiPropertyKeys.ValueType(this, propertyName);

    protected void SetDefaultProperty(string name, object? value, UiDirtyFlags invalidation) =>
        _properties.InitializeDefault(name, value, invalidation);
    protected void SetLocalProperty(string name, object? value, UiDirtyFlags invalidation) =>
        _properties.SetLocal(name, value, invalidation);

    internal void SetAppliedStyle(Delta.XAML.UiStyle style, Delta.XAML.UiStyleState state, int version)
    {
        ArgumentNullException.ThrowIfNull(style);
        _appliedStyle = style;
        _appliedStyleState = state;
        _appliedStyleVersion = version;
    }

    internal void ClearAppliedStyle()
    {
        _appliedStyle = null;
        _appliedStyleState = default;
        _appliedStyleVersion = -1;
    }

    internal void ClearStyleValue(string name) => _properties.Clear(name, UiValueSource.Style);

    internal void SetCompiledTemplate(Delta.XAML.UiTemplateId template)
    {
        if (!template.IsValid || _compiledTemplateId == template)
        {
            return;
        }

        _compiledTemplateId = template;
        InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }

    internal void SetCompiledStyle(Guid style)
    {
        if (style == Guid.Empty || _compiledStyleId == style)
        {
            return;
        }

        _compiledStyleId = style;
        InvalidateChanged(UiDirtyFlags.Style | UiDirtyFlags.Measure | UiDirtyFlags.Visual);
    }
}
