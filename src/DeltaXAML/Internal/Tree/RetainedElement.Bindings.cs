using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal partial class UiElement
{
    internal IReadOnlyList<UiBindingSpec> BindingSpecs => _bindingSpecs;
    internal object? BindingContext => _bindingContext;
    internal bool HasExplicitBindingContext => _hasExplicitBindingContext;

    internal void AddBindingSpec(in UiBindingSpec spec) => _bindingSpecs.Add(spec);

    internal void AttachBinding(UiInterpretedBinding binding)
    {
        if (_externalBindingRuntimes.Remove(binding.PropertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(binding.PropertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(binding.PropertyName, out var compiledPrevious))
        {
            compiledPrevious.Dispose();
            _properties.Clear(binding.PropertyName, UiValueSource.Binding);
        }

        if (_bindingRuntimes.Remove(binding.PropertyName, out var previous))
        {
            previous.Dispose();
        }

        _bindingRuntimes.Add(binding.PropertyName, binding);
        if (_bindingStageManaged)
        {
            binding.EnableStageManagement();
        }

        binding.Attach(this, BindingInvalidation(binding.PropertyName));
        binding.SetContext(_bindingContext);
    }

    internal void AttachExternalBinding(string propertyName, Delta.XAML.IUiBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        if (_bindingRuntimes.Remove(propertyName, out var interpretedPrevious))
        {
            interpretedPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_externalBindingRuntimes.Remove(propertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(propertyName, out var compiledPrevious))
        {
            compiledPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        var runtime = new UiExternalBindingRuntime(
            this,
            propertyName,
            UiPropertyKeys.Resolve(propertyName),
            BindingInvalidation(propertyName),
            binding);
        _externalBindingRuntimes.Add(propertyName, runtime);
        if (_bindingStageManaged)
        {
            runtime.EnableStageManagement();
        }

        runtime.Attach();
    }

    internal void AttachCompiledBinding<TSource, TValue>(
        string propertyName,
        UiPropertyKey property,
        UiDirtyFlags invalidation,
        Delta.XAML.UiCompiledBinding<TSource, TValue> binding,
        bool sourceNotificationsManaged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        if (_externalBindingRuntimes.Remove(propertyName, out var externalPrevious))
        {
            externalPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_bindingRuntimes.Remove(propertyName, out var interpretedPrevious))
        {
            interpretedPrevious.Dispose();
            _properties.Clear(propertyName, UiValueSource.Binding);
        }

        if (_compiledBindingRuntimes.Remove(propertyName, out var previous))
        {
            previous.Dispose();
        }

        var runtime = new UiCompiledBindingRuntime<TSource, TValue>(
            this,
            propertyName,
            property,
            invalidation,
            binding,
            sourceNotificationsManaged);
        if (binding.Mode == Delta.XAML.UiBindingMode.OneTime)
        {
            runtime.Attach();
            runtime.Dispose();
            return;
        }

        _compiledBindingRuntimes.Add(propertyName, runtime);
        runtime.Attach();
    }

    internal void ApplyCompiledBinding(
        string propertyName,
        UiPropertyKey property,
        object? value,
        UiDirtyFlags invalidation) =>
        _properties.SetBindingValue(propertyName, property, value, invalidation);

    internal void ApplyExternalBinding(
        string propertyName,
        UiPropertyKey property,
        object? value,
        UiDirtyFlags invalidation) =>
        _properties.SetBindingValue(propertyName, property, Delta.XAML.UiElement.ToRetainedValue(value), invalidation);

    internal void ApplyTemplateOwnerBindings()
    {
        foreach (var binding in _externalBindingRuntimes.Values)
        {
            if (binding.IsTemplateOwnerRelation)
            {
                binding.ApplyPending();
            }
        }
    }

    internal void QueueCompiledBindingRefresh(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (_compiledBindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.QueueRefresh();
        }
    }

    internal bool HasBinding(string propertyName) =>
        _bindingRuntimes.ContainsKey(propertyName) ||
        _externalBindingRuntimes.ContainsKey(propertyName) ||
        _compiledBindingRuntimes.ContainsKey(propertyName);

    internal void ApplyBindingStage()
    {
        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.ApplyPending();
        }

        foreach (var binding in _externalBindingRuntimes.Values)
        {
            binding.ApplyPending();
        }

        foreach (var binding in _compiledBindingRuntimes.Values)
        {
            binding.ApplyPending();
        }
    }

    internal bool NeedsBindingStage => !_bindingStageManaged || (DirtyFlags & (UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree)) != 0;
    internal void CompleteBindingStage() => DirtyFlags &= ~(UiDirtyFlags.Binding | UiDirtyFlags.BindingSubtree);

    internal void EnableBindingStage()
    {
        _bindingStageManaged = true;
        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.EnableStageManagement();
        }

        foreach (var binding in _externalBindingRuntimes.Values)
        {
            binding.EnableStageManagement();
        }
    }

    internal void NotifyBindingTargetChanged(string propertyName, object? value)
    {
        if (_bindingRuntimes.TryGetValue(propertyName, out var binding))
        {
            binding.WriteTarget(value);
        }

        if (_externalBindingRuntimes.TryGetValue(propertyName, out var externalBinding))
        {
            externalBinding.WriteTarget(value);
        }

        if (_compiledBindingRuntimes.TryGetValue(propertyName, out var compiledBinding))
        {
            compiledBinding.TryWrite(value, out _);
        }
    }

    internal void NotifyEffectivePropertyChanged(string propertyName) => EffectivePropertyChanged?.Invoke(propertyName);


    private static UiDirtyFlags BindingInvalidation(string propertyName) => propertyName switch
    {
        "Text" or "FontKey" or "FontSize" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "Width" or "Height" or "Margin" or "Padding" => UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "HorizontalAlignment" or "VerticalAlignment" => UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "Foreground" or "OutlineColor" or "OutlineWidth" or "TextEffect" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "EffectSet" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "BorderColor" or "BorderWidth" or "BorderWidthUnits" or "CornerRadius" => UiDirtyFlags.Visual,
        _ => UiDirtyFlags.Visual,
    };

    internal void SetBindingContext(object? value, bool explicitValue)
    {
        if (_nodeStore is { } store)
        {
            store.SetBindingContext(this, value, explicitValue);
            return;
        }

        var traversal = new List<UiElement> { this };
        for (var i = 0; i < traversal.Count; i++)
        {
            var element = traversal[i];
            var isOwner = i == 0;
            if (!isOwner && element._hasExplicitBindingContext)
            {
                continue;
            }

            element.ApplyBindingContext(value, isOwner && explicitValue);
            for (var childIndex = 0; childIndex < element._detachedChildren.Count; childIndex++)
            {
                traversal.Add(element._detachedChildren[childIndex]);
            }
        }
    }

    internal void ApplyBindingContext(object? value, bool explicitValue)
    {
        _bindingContext = value;
        if (explicitValue)
        {
            _hasExplicitBindingContext = true;
        }

        foreach (var binding in _bindingRuntimes.Values)
        {
            binding.SetContext(value);
        }
    }
}
