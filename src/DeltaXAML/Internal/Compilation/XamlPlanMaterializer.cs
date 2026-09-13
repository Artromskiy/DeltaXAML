using System.Globalization;
using System.Collections.Immutable;
using System.ComponentModel;
using Delta;
using Delta.Diagnostics;
using DeltaXAML.Compiler;
using Delta.XAML.Contract;
using Delta.XAML;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;
namespace DeltaXAML.Internal;

internal readonly record struct XamlMaterializerDiagnostic(string Code, string Message, int Line, int Column);
internal sealed record XamlMaterializerResult(
    UiElement? Root,
    IReadOnlyList<XamlMaterializerDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, UiElement>? Names = null);
/// <summary>Cold materializer selected only by the explicit <c>IXamlLoader</c> API.</summary>
/// <remarks>It consumes the compiler's semantic plan and constructs canonical descriptor-backed elements; generated production artifacts bypass XML inflation.</remarks>
internal static class XamlPlanMaterializer
{
    internal static XamlMaterializerResult Read(
        XamlDocumentPlan plan,
        Func<string, string, UiElement?>? factory,
        UiResourceStore? resources,
        Dictionary<UiElement, Delta.XAML.UiElement>? views = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var diagnostics = new List<XamlMaterializerDiagnostic>();
        var names = new Dictionary<string, UiElement>(StringComparer.Ordinal);
        MaterializeResourceDeclarations(plan, factory, resources, diagnostics, views);
        if (plan.Root is null)
        {
            return new(null, diagnostics, names);
        }

        if (string.Equals(plan.Root.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
        {
            return new(null, diagnostics, names);
        }

        var root = Materialize(plan.Source, plan.Root, factory, resources, diagnostics, names);
        return new(root, diagnostics, names);
    }

    /// <summary>Materializes styles and templates declared by a cold document into its resource-backed theme.</summary>
    internal static UiTheme? MaterializeTheme(
        XamlDocumentPlan plan,
        UiResourceCatalog catalog,
        List<Diagnostic> diagnostics,
        Func<string, string, UiElement?>? factory = null,
        Dictionary<UiElement, Delta.XAML.UiElement>? views = null,
        IUiBindingResolver? bindings = null,
        IUiTemplateSelectorResolver? selectors = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (plan.Styles.Length == 0 && plan.Templates.Length == 0 &&
            plan.Triggers.Length == 0 && plan.Behaviors.Length == 0)
        {
            return null;
        }

        var theme = new UiTheme(catalog);
        if (plan.Triggers.Length != 0 || plan.Behaviors.Length != 0)
        {
            diagnostics.Add(new Diagnostic(new DiagnosticCode("XAML020"), DiagnosticSeverity.Error,
                "Triggers and Behaviors require the generated XAML path.", null));
        }

        var styles = new UiStyle[plan.Styles.Length];
        for (var styleIndex = 0; styleIndex < plan.Styles.Length; styleIndex++)
        {
            var stylePlan = plan.Styles[styleIndex];
            styles[styleIndex] = stylePlan.TargetTypeId.IsValid
                ? new UiStyle(stylePlan.Key, stylePlan.TargetTypeId, catalog)
                : new UiStyle(stylePlan.Key, stylePlan.TargetType.LocalName, catalog);
            if (stylePlan.Variant is { } variant)
            {
                styles[styleIndex].SetVariant(variant);
            }
        }

        for (var styleIndex = 0; styleIndex < plan.Styles.Length; styleIndex++)
        {
            var stylePlan = plan.Styles[styleIndex];
            if (stylePlan.BasedOn is { } basedOn)
            {
                var baseIndex = FindStyle(plan.Styles, basedOn, null);
                if (baseIndex < 0)
                {
                    baseIndex = FindStyle(plan.Styles, basedOn, stylePlan.Variant);
                }

                if (baseIndex < 0)
                {
                    diagnostics.Add(new Diagnostic(new DiagnosticCode("XAML036"), DiagnosticSeverity.Error,
                        $"Style '{stylePlan.Key}' is BasedOn '{basedOn}', but that style is not declared.", null));
                }
                else
                {
                    styles[styleIndex].SetBasedOn(styles[baseIndex]);
                }
            }

            ApplyStyleSetters(styles[styleIndex], stylePlan.Setters, catalog, diagnostics);
            for (var stateIndex = 0; stateIndex < stylePlan.VisualStates.Length; stateIndex++)
            {
                var state = stylePlan.VisualStates[stateIndex];
                var stateValue = ToStyleState(state.State);
                if (stateValue is UiStyleState.None or UiStyleState.Unknown)
                {
                    continue;
                }

                ApplyStyleSetters(styles[styleIndex], stateValue, state.Setters, catalog, diagnostics);
            }

            theme.Add(styles[styleIndex]);
        }

        for (var templateIndex = 0; templateIndex < plan.Templates.Length; templateIndex++)
        {
            var templatePlan = plan.Templates[templateIndex];
            theme.RegisterTemplate(templatePlan.Key, new UiTemplate(new ColdTemplateFactory(templatePlan, factory, views, bindings, selectors, theme)));
        }

        return theme;
    }

    private static void MaterializeResourceDeclarations(
        XamlDocumentPlan plan,
        Func<string, string, UiElement?>? factory,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics,
        Dictionary<UiElement, Delta.XAML.UiElement>? views)
    {
        var hasDeclarations = plan.Resources.Length != 0 || plan.ScalarResources.Length != 0 ||
            plan.GradientResources.Length != 0 || plan.EffectResources.Length != 0;
        if (!hasDeclarations)
        {
            return;
        }

        if (resources is null)
        {
            diagnostics.Add(new("XAML020", "Inline resources require a mutable UiResourceCatalog in the cold loader or the generated XAML path.", 1, 1));
            return;
        }

        for (var scalarIndex = 0; scalarIndex < plan.ScalarResources.Length; scalarIndex++)
        {
            var scalar = plan.ScalarResources[scalarIndex];
            if (!TryMaterializeLiteral(scalar.Value, scalar.Key, out var value))
            {
                diagnostics.Add(new("XAML035", $"Scalar Resource '{scalar.Key}' has an unsupported value.", scalar.Range.Start.Line + 1, scalar.Range.Start.Column + 1));
                continue;
            }

            resources.Set(scalar.Id.Value, value);
            resources.Set(scalar.Key, new UiResourceReference(scalar.Id.Value));
        }

        for (var gradientIndex = 0; gradientIndex < plan.GradientResources.Length; gradientIndex++)
        {
            var gradient = plan.GradientResources[gradientIndex];
            if (!TryMaterializeGradient(gradient, resources, out var value, out var error))
            {
                diagnostics.Add(new("XAML057", error, gradient.Range.Start.Line + 1, gradient.Range.Start.Column + 1));
                continue;
            }

            resources.Set(gradient.PayloadId.Value, value);
            resources.Set(gradient.Key, new UiResourceReference(gradient.Id.Value));
            resources.Set(gradient.Id.Value, gradient.Kind == XamlGradientKind.Linear
                ? UiBrush.LinearGradient(gradient.PayloadId)
                : UiBrush.RadialGradient(gradient.PayloadId));
        }

        for (var resourceIndex = 0; resourceIndex < plan.Resources.Length; resourceIndex++)
        {
            var resourcePlan = plan.Resources[resourceIndex];
            var resourceRoot = Materialize(plan.Source, resourcePlan.Value, factory, resources, diagnostics, names: null);
            if (resourceRoot is null)
            {
                continue;
            }

            resources.Set(resourcePlan.Id.Value, Delta.XAML.UiElement.Wrap(resourceRoot, views));
            resources.Set(resourcePlan.Key, new UiResourceReference(resourcePlan.Id.Value));
        }

        for (var effectIndex = 0; effectIndex < plan.EffectResources.Length; effectIndex++)
        {
            var effectPlan = plan.EffectResources[effectIndex];
            if (TryMaterializeEffect(effectPlan, diagnostics, out var effect))
            {
                resources.Set(effect.Set.Resource.Value, effect);
                if (effectPlan.Key is { } key)
                {
                    resources.Set(key, new UiResourceReference(effect.Set.Resource.Value));
                }
            }
        }
    }

    private static int FindStyle(IReadOnlyList<XamlStylePlan> styles, string key, string? variant)
    {
        for (var index = 0; index < styles.Count; index++)
        {
            if (string.Equals(styles[index].Key, key, StringComparison.Ordinal) &&
                string.Equals(styles[index].Variant, variant, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ApplyStyleSetters(
        UiStyle style,
        ImmutableArray<XamlMemberPlan> setters,
        UiResourceCatalog resources,
        List<Diagnostic> diagnostics) => ApplyStyleSetters(style, UiStyleState.None, setters, resources, diagnostics);

    private static void ApplyStyleSetters(
        UiStyle style,
        UiStyleState state,
        ImmutableArray<XamlMemberPlan> setters,
        UiResourceCatalog resources,
        List<Diagnostic> diagnostics)
    {
        for (var setterIndex = 0; setterIndex < setters.Length; setterIndex++)
        {
            var setter = setters[setterIndex];
            if (setter.Value.Kind == XamlValueKind.ResourceReference)
            {
                var resource = setter.Value.Resource;
                if (state == UiStyleState.None)
                {
                    if (resource.IsDynamic)
                    {
                        style.SetResource(setter.Name, resource.Key);
                    }
                    else
                    {
                        style.SetStaticResource(setter.Name, resource.Key);
                    }
                }
                else if (resource.IsDynamic)
                {
                    style.SetStateResource(state, setter.Name, resource.Key);
                }
                else
                {
                    style.SetStateStaticResource(state, setter.Name, resource.Key);
                }

                continue;
            }

            if (!TryMaterializeLiteral(setter.Value, setter.Name, out var value))
            {
                diagnostics.Add(new Diagnostic(new DiagnosticCode("XAML002"), DiagnosticSeverity.Error,
                    $"Style setter '{setter.Name}' has an unsupported value.", null));
                continue;
            }

            if (state == UiStyleState.None)
            {
                style.Set(setter.Name, value);
            }
            else
            {
                style.SetState(state, setter.Name, value);
            }
        }
    }

    private static UiStyleState ToStyleState(XamlVisualStateName state) => state switch
    {
        XamlVisualStateName.Normal => UiStyleState.Normal,
        XamlVisualStateName.Hover => UiStyleState.Hover,
        XamlVisualStateName.Pressed => UiStyleState.Pressed,
        XamlVisualStateName.Focused => UiStyleState.Focused,
        XamlVisualStateName.Disabled => UiStyleState.Disabled,
        XamlVisualStateName.Invalid => UiStyleState.Invalid,
        XamlVisualStateName.Selected => UiStyleState.Selected,
        _ => UiStyleState.Unknown,
    };

    private sealed class ColdTemplateFactory(
        XamlTemplatePlan plan,
        Func<string, string, UiElement?>? factory,
        Dictionary<UiElement, Delta.XAML.UiElement>? views,
        IUiBindingResolver? bindings,
        IUiTemplateSelectorResolver? selectors,
        UiTheme theme) : IUiTemplateFactory
    {
        public Delta.XAML.UiElement Create(Delta.XAML.UiElement owner, UiResourceCatalog resources)
        {
            var diagnostics = new List<XamlMaterializerDiagnostic>();
            var names = new Dictionary<string, UiElement>(StringComparer.Ordinal);
            var root = Materialize(SourceId.Empty, plan.Root, factory, resources.Store, diagnostics, names);
            if (root is null || diagnostics.Count != 0)
            {
                throw new InvalidOperationException($"Template '{plan.Key}' failed to materialize.");
            }

            var wrapped = Delta.XAML.UiElement.Wrap(root, views);
            wrapped.BindingContext = owner.BindingContext;
            AttachBindingSpecs(root, bindings, diagnostics, names, owner.RetainedElement, theme, resources, selectors);
            if (diagnostics.Count != 0)
            {
                throw new InvalidOperationException($"Template '{plan.Key}' failed to attach interpreted bindings.");
            }

            return wrapped;
        }
    }

    /// <summary>Interpreted collection adapter. It keeps the source contract typed at the boundary and realizes a bounded range.</summary>
    internal sealed class UiInterpretedCollectionBinding : IDisposable
    {
        private readonly UiElement _target;
        private readonly UiCollectionBindingSpec _spec;
        private readonly UiTheme _theme;
        private readonly UiResourceCatalog _resources;
        private readonly IUiTemplateSelectorResolver? _selectors;
        private object? _context;
        private INotifyPropertyChanged? _observable;
        private ulong _version = ulong.MaxValue;
        private object? _source;
        private bool _pending;
        private bool _stageManaged;
        private bool _disposed;

        internal UiInterpretedCollectionBinding(
            UiElement target,
            UiCollectionBindingSpec spec,
            UiTheme theme,
            UiResourceCatalog resources,
            IUiTemplateSelectorResolver? selectors = null)
        {
            _target = target;
            _spec = spec;
            _theme = theme;
            _resources = resources;
            _selectors = selectors;
        }

        internal void Attach(UiElement owner)
        {
            _ = owner;
            SetContext(_target.BindingContext);
        }

        internal void SetContext(object? context)
        {
            if (ReferenceEquals(_context, context))
            {
                QueueRefresh();
                return;
            }

            if (_observable is not null)
            {
                _observable.PropertyChanged -= OnPropertyChanged;
            }

            _context = context;
            _observable = context as INotifyPropertyChanged;
            if (_observable is not null)
            {
                _observable.PropertyChanged += OnPropertyChanged;
            }

            QueueRefresh();
        }

        internal void EnableStageManagement() => _stageManaged = true;

        internal void ApplyPending()
        {
            if (!_pending || _disposed)
            {
                return;
            }

            _pending = false;
            Apply();
        }

        internal void Poll()
        {
            if (_disposed || _spec.Mode == UiBindingMode.OneTime || _pending)
            {
                return;
            }

            var source = ReadPath(_context, _spec.Path);
            var sourceType = source?.GetType().GetInterfaces().FirstOrDefault(static type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IUiItemsSource<>));
            if (source is null || sourceType is null)
            {
                if (_source is not null)
                {
                    QueueRefresh();
                }

                return;
            }

            var version = ReadUInt64(sourceType.GetProperty("Version")?.GetValue(source));
            if (!ReferenceEquals(_source, source) || _version != version)
            {
                QueueRefresh();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_observable is not null)
            {
                _observable.PropertyChanged -= OnPropertyChanged;
            }
        }

        private void QueueRefresh()
        {
            if (_disposed)
            {
                return;
            }

            if (!_stageManaged)
            {
                Apply();
                return;
            }

            _pending = true;
            _target.Invalidate(UiDirtyFlags.Binding);
        }

        private void Apply()
        {
            var source = ReadPath(_context, _spec.Path);
            var sourceType = source?.GetType().GetInterfaces().FirstOrDefault(static type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IUiItemsSource<>));
            if (source is null || sourceType is null)
            {
                if (_source is not null)
                {
                    SetItems(Array.Empty<object?>());
                    _source = null;
                    _version = ulong.MaxValue;
                }

                return;
            }

            var version = ReadUInt64(sourceType.GetProperty("Version")?.GetValue(source));
            if (ReferenceEquals(_source, source) && _version == version)
            {
                return;
            }

            var count = Convert.ToInt32(sourceType.GetProperty("Count")?.GetValue(source) ?? 0, CultureInfo.InvariantCulture);
            var start = Maths.Clamp(_spec.VirtualizationStart, 0, count);
            var requested = _spec.VirtualizationCount < 0 ? count - start : Maths.Min(_spec.VirtualizationCount, count - start);
            var values = new object?[Maths.Max(0, requested)];
            var getItem = sourceType.GetMethod("GetItem") ?? throw new InvalidOperationException($"Items source '{sourceType.Name}' does not expose GetItem.");
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = getItem.Invoke(source, [start + index]);
            }

            _source = source;
            _version = version;
            SetItems(values);
        }

        private void SetItems(IReadOnlyList<object?> values)
        {
            var items = FindItemsHost();
            if (items is null)
            {
                return;
            }

            UiTemplate? staticTemplate = null;
            if (_spec.ItemTemplateSelector is { Length: > 0 } selectorKey)
            {
                if (_selectors is null)
                {
                    throw new InvalidOperationException($"Interpreted item-template selector '{selectorKey}' requires a registered selector resolver.");
                }
            }
            else if (_spec.ItemTemplate is not { Length: > 0 } templateKey || !_theme.TryGetTemplate(templateKey, out staticTemplate))
            {
                throw new InvalidOperationException($"Interpreted collection markup requires a declared template '{_spec.ItemTemplate ?? string.Empty}'.");
            }

            items.SetItems(values, item =>
            {
                var selectedTemplate = staticTemplate;
                if (_spec.ItemTemplateSelector is { Length: > 0 } selectorKey)
                {
                    if (_selectors is null || !_selectors.TrySelectTemplate(selectorKey, item, out var selectedKey) ||
                        string.IsNullOrWhiteSpace(selectedKey) || !_theme.TryGetTemplate(selectedKey, out selectedTemplate))
                    {
                        throw new InvalidOperationException($"Interpreted item-template selector '{selectorKey}' did not select a declared template.");
                    }
                }

                var owner = Delta.XAML.UiElement.Wrap(_target);
                var created = selectedTemplate!.Build(owner, _resources);
                created.BindingContext = item;
                return created.RetainedElement;
            });
        }

        private ItemsControl? FindItemsHost()
        {
            if (_target is ItemsControl itemsControl)
            {
                return itemsControl;
            }

            if (_target is CollectionView && _target.Children.Count > 0 && _target.Children[0] is ScrollViewer { Content: ItemsControl items })
            {
                return items;
            }

            if (_target is Picker && _target.Children.Count > 1 && _target.Children[1] is Overlay { Children.Count: > 0 } overlay &&
                overlay.Children[0] is CollectionView collection && collection.Children.Count > 0 &&
                collection.Children[0] is ScrollViewer { Content: ItemsControl pickerItems })
            {
                return pickerItems;
            }

            return null;
        }

        private static object? ReadPath(object? source, string path)
        {
            var value = source;
            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var index = 0; index < segments.Length; index++)
            {
                if (value is null)
                {
                    return null;
                }

                var property = value.GetType().GetProperty(segments[index]);
                if (property is null || !property.CanRead)
                {
                    return null;
                }

                value = property.GetValue(value);
            }

            return value;
        }

        private static ulong ReadUInt64(object? value) => value switch
        {
            ulong result => result,
            uint result => result,
            int result when result >= 0 => (ulong)result,
            _ => 0,
        };

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            _ = sender;
            if (_spec.Mode != UiBindingMode.OneTime &&
                (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == _spec.Path.Split('.')[0]))
            {
                QueueRefresh();
            }
        }
    }

    private static void AttachBindingSpecs(
        UiElement element,
        IUiBindingResolver? resolver,
        List<XamlMaterializerDiagnostic> diagnostics,
        IReadOnlyDictionary<string, UiElement>? names = null,
        UiElement? templateOwner = null,
        UiTheme? theme = null,
        UiResourceCatalog? resources = null,
        IUiTemplateSelectorResolver? selectors = null)
    {
        foreach (var spec in element.BindingSpecs)
        {
            IUiValueConverter? converter = null;
            if (spec.ConverterKey is not null && (resolver is null || !resolver.TryResolveConverter(spec.ConverterKey, out converter)))
            {
                diagnostics.Add(new("XAML009", $"Binding converter '{spec.ConverterKey}' was not registered.", 1, 1));
                continue;
            }

            var expression = new UiBindingExpression(
                spec.Path,
                BindingModeMap.ToPublic(spec.Mode),
                converter,
                spec.StringFormat,
                spec.CultureName is null ? null : CultureInfo.GetCultureInfo(spec.CultureName));
            if (spec.SourceKind == Delta.XAML.UiBindingSourceKind.Context)
            {
                element.AttachBinding(new UiInterpretedBinding(spec.Property, expression));
            }
            else
            {
                element.AttachExternalBinding(
                    spec.Property,
                    new UiInterpretedRelationBinding(
                        element,
                        spec.SourceKind,
                        spec.SourceArgument,
                        spec.Path,
                        BindingModeMap.ToPublic(spec.Mode),
                        converter,
                        spec.StringFormat,
                        spec.CultureName is null ? null : CultureInfo.GetCultureInfo(spec.CultureName),
                        names,
                        templateOwner));
            }
        }

        for (var bindingIndex = 0; bindingIndex < element.MultiBindingSpecs.Count; bindingIndex++)
        {
            var spec = element.MultiBindingSpecs[bindingIndex];
            if (spec.FunctionKey is not null && resolver is not IUiBindingFunctionResolver)
            {
                diagnostics.Add(new("XAML020", $"MultiBinding function '{spec.FunctionKey}' is not registered for the cold path.", 1, 1));
                continue;
            }

            element.AttachExternalBinding(
                spec.Property,
                new UiInterpretedMultiBinding(
                    element,
                    spec.Sources,
                    spec.FunctionKey,
                    spec.StringFormat,
                    spec.CultureName is null ? null : CultureInfo.GetCultureInfo(spec.CultureName),
                    names,
                    templateOwner,
                    resolver as IUiBindingFunctionResolver));
        }

        for (var collectionIndex = 0; collectionIndex < element.CollectionBindingSpecs.Count; collectionIndex++)
        {
            if (theme is null || resources is null)
            {
                diagnostics.Add(new("XAML020", "Collection markup requires a materialized theme and resource catalog in the cold path.", 1, 1));
                continue;
            }

            var collection = element.CollectionBindingSpecs[collectionIndex];
            if (collection.ItemTemplate is null && collection.ItemTemplateSelector is null)
            {
                diagnostics.Add(new("XAML020", "Interpreted collection markup requires ItemTemplate or ItemTemplateSelector.", 1, 1));
                continue;
            }

            if (collection.ItemTemplate is not null && !theme.TryGetTemplate(collection.ItemTemplate, out _))
            {
                diagnostics.Add(new("XAML020", $"Interpreted collection markup references undeclared ItemTemplate '{collection.ItemTemplate}'.", 1, 1));
                continue;
            }

            if (collection.ItemTemplateSelector is not null && selectors is null)
            {
                diagnostics.Add(new("XAML020", $"Interpreted item-template selector '{collection.ItemTemplateSelector}' requires a registered selector resolver.", 1, 1));
                continue;
            }

            element.AttachCollectionBinding(new UiInterpretedCollectionBinding(
                element,
                collection,
                theme,
                resources,
                selectors));
        }

        for (var index = 0; index < element.Children.Count; index++)
        {
            AttachBindingSpecs((UiElement)element.Children[index], resolver, diagnostics, names, templateOwner, theme, resources, selectors);
        }
    }

    private static bool TryMaterializeLiteral(
        XamlValuePlan value,
        string propertyName,
        out object? result)
    {
        result = null;
        var literal = value.Literal.CanonicalText;
        switch (value.Kind)
        {
            case XamlValueKind.String:
                result = literal;
                return true;
            case XamlValueKind.Boolean when bool.TryParse(literal, out var boolean):
                result = boolean;
                return true;
            case XamlValueKind.Single when float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) && float.IsFinite(single):
                result = single;
                return true;
            case XamlValueKind.Double when double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) && double.IsFinite(doubleValue):
                result = doubleValue;
                return true;
            case XamlValueKind.Integer when int.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                result = integer;
                return true;
            case XamlValueKind.ResourceId when Guid.TryParse(literal, out var resourceId) && resourceId != Guid.Empty:
                result = new UiResourceId(resourceId);
                return true;
            case XamlValueKind.Color when TryColor(literal, out var color):
                result = new Delta.XAML.UiColor(color.R, color.G, color.B, color.A);
                return true;
            case XamlValueKind.Brush when TryBrush(literal, out var brush):
                result = brush;
                return true;
            case XamlValueKind.Thickness when TryThickness(literal, out var thickness):
                result = thickness;
                return true;
            case XamlValueKind.GridLengthList when TryGridLengths(literal, out var gridLengths):
                result = gridLengths;
                return true;
            case XamlValueKind.CornerRadii when TryCornerRadii(literal, out var radii):
                result = radii;
                return true;
            case XamlValueKind.Enum:
                return TryMaterializeEnum(propertyName, literal, out result);
            default:
                return false;
        }
    }

    private static bool TryMaterializeEnum(string propertyName, string literal, out object? result)
    {
        result = null;
        switch (propertyName)
        {
            case "Orientation" when Enum.TryParse(literal, true, out UiOrientation orientation):
                result = orientation;
                return true;
            case "BorderWidthUnits" when Enum.TryParse(literal, true, out PaintUnits units) && units is PaintUnits.Logical or PaintUnits.Device:
                result = units;
                return true;
            case "BlendMode" when Enum.TryParse(literal, true, out UiBlendMode blendMode) && blendMode is UiBlendMode.Opaque or UiBlendMode.Alpha or UiBlendMode.PremultipliedAlpha or UiBlendMode.Additive or UiBlendMode.Multiply:
                result = blendMode;
                return true;
            case "HorizontalAlignment" when Enum.TryParse(literal, true, out Delta.XAML.UiHorizontalAlignment horizontal) && horizontal != Delta.XAML.UiHorizontalAlignment.Unknown:
                result = horizontal;
                return true;
            case "VerticalAlignment" when Enum.TryParse(literal, true, out Delta.XAML.UiVerticalAlignment vertical) && vertical != Delta.XAML.UiVerticalAlignment.Unknown:
                result = vertical;
                return true;
            case "HorizontalTextAlignment" when Enum.TryParse(literal, true, out Delta.XAML.UiTextHorizontalAlignment horizontalText) && horizontalText != Delta.XAML.UiTextHorizontalAlignment.Unknown:
                result = horizontalText;
                return true;
            case "VerticalTextAlignment" when Enum.TryParse(literal, true, out Delta.XAML.UiTextVerticalAlignment verticalText) && verticalText != Delta.XAML.UiTextVerticalAlignment.Unknown:
                result = verticalText;
                return true;
            case "TextWrapping" when Enum.TryParse(literal, true, out Delta.XAML.UiTextWrapping wrapping) && wrapping != Delta.XAML.UiTextWrapping.Unknown:
                result = wrapping;
                return true;
            case "TextTrimming" when Enum.TryParse(literal, true, out Delta.XAML.UiTextTrimming trimming) && trimming != Delta.XAML.UiTextTrimming.Unknown:
                result = trimming;
                return true;
            case "FontWeight" when Enum.TryParse(literal, true, out Delta.XAML.UiFontWeight weight) && weight != Delta.XAML.UiFontWeight.Unknown:
                result = weight;
                return true;
            case "FontStyle" when Enum.TryParse(literal, true, out Delta.XAML.UiFontStyle style) && style != Delta.XAML.UiFontStyle.Unknown:
                result = style;
                return true;
            case "TextDecorations" when TryTextDecorations(literal, out var decorations):
                result = decorations;
                return true;
            case "AutomationRole" when Enum.TryParse(literal, true, out Delta.XAML.UiSemanticRole role):
                result = role;
                return true;
            case "Gestures" when TryGestureKind(literal, out var gestures):
                result = gestures;
                return true;
            case "Stretch" when Enum.TryParse(literal, true, out Delta.XAML.UiImageStretch stretch):
                result = stretch;
                return true;
            default:
                return false;
        }
    }

    private static bool TryMaterializeGradient(
        XamlGradientResourcePlan plan,
        UiResourceStore resources,
        out object value,
        out string error)
    {
        value = default!;
        error = string.Empty;
        var stops = new UiGradientStop[plan.Stops.Length];
        for (var index = 0; index < stops.Length; index++)
        {
            var stop = plan.Stops[index];
            if (!TryGradientColor(stop.Color, resources, out var color))
            {
                error = $"Gradient stop at offset {stop.Offset.ToString(CultureInfo.InvariantCulture)} is not a color.";
                return false;
            }

            stops[index] = new(stop.Offset, color);
        }

        try
        {
            if (plan.Kind == XamlGradientKind.Linear)
            {
                UiLinearGradient gradient;
                if (plan.StartPoint is { } start && plan.EndPoint is { } end &&
                    TryGradientVector(start.Literal.CanonicalText, out var startX, out var startY) &&
                    TryGradientVector(end.Literal.CanonicalText, out var endX, out var endY))
                {
                    gradient = new UiLinearGradient(startX, startY, endX, endY, stops);
                }
                else
                {
                    var angle = plan.Angle is { } anglePlan && float.TryParse(anglePlan.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedAngle)
                        ? parsedAngle
                        : 180;
                    gradient = UiLinearGradient.Relative(angle, stops);
                }

                if (plan.OutlineColor is { } outline && TryGradientColor(outline, resources, out var outlineColor))
                {
                    var width = plan.OutlineWidth is { } widthPlan && float.TryParse(widthPlan.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth)
                        ? parsedWidth
                        : 1;
                    gradient = gradient.WithOutline(outlineColor, width);
                }

                value = gradient;
                return true;
            }

            if (plan.Center is not { } center || plan.Radius is not { } radius ||
                !TryGradientVector(center.Literal.CanonicalText, out var centerX, out var centerY) ||
                !TryGradientVector(radius.Literal.CanonicalText, out var radiusX, out var radiusY))
            {
                error = "Radial gradient geometry is invalid.";
                return false;
            }

            var radial = new UiRadialGradient(centerX, centerY, radiusX, radiusY, stops, plan.Units);
            if (plan.OutlineColor is { } radialOutline && TryGradientColor(radialOutline, resources, out var radialOutlineColor))
            {
                var width = plan.OutlineWidth is { } widthPlan && float.TryParse(widthPlan.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth)
                    ? parsedWidth
                    : 1;
                radial = radial.WithOutline(radialOutlineColor, width);
            }

            value = radial;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryGradientColor(XamlValuePlan value, UiResourceStore resources, out Delta.XAML.UiColor color)
    {
        if (value.Kind == XamlValueKind.ResourceReference)
        {
            if (value.Resource.IsDynamic || !resources.TryResolve(new UiResourceReference(value.Resource.Key, value.Resource.Id.Value), out var resolved, out _))
            {
                color = default;
                return false;
            }

            return TryPublicColor(resolved, out color);
        }

        if (TryColor(value.Literal.CanonicalText, out var parsed))
        {
            color = new(parsed.R, parsed.G, parsed.B, parsed.A);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryPublicColor(object? value, out Delta.XAML.UiColor color)
    {
        switch (value)
        {
            case Delta.XAML.UiColor publicColor:
                color = publicColor;
                return true;
            case UiColor retainedColor:
                color = new(retainedColor.R, retainedColor.G, retainedColor.B, retainedColor.A);
                return true;
            default:
                color = default;
                return false;
        }
    }

    private static bool TryGradientVector(string value, out float x, out float y)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        x = y = float.NaN;
        if (parts.Length is not (1 or 2) || !TryGradientComponent(parts[0], out x))
        {
            return false;
        }

        y = parts.Length == 1 ? x : float.NaN;
        return parts.Length == 1 || TryGradientComponent(parts[1], out y);
    }

    private static bool TryGradientComponent(string value, out float component)
    {
        var percent = value.EndsWith('%');
        var numeric = percent ? value[..^1].Trim() : value;
        if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out component) || !float.IsFinite(component))
        {
            return false;
        }

        if (percent)
        {
            component /= 100;
        }
        return float.IsFinite(component);
    }

    private static UiElement? Materialize(
        SourceId source,
        XamlObjectPlan plan,
        Func<string, string, UiElement?>? factory,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics,
        Dictionary<string, UiElement>? names)
    {
        var element = UiBuiltInElementFactory.TryCreate(plan.Name.LocalName) ??
            factory?.Invoke(plan.Name.Namespace, plan.Name.LocalName);
        if (element is null)
        {
            diagnostics.Add(new(
                "XAML002",
                $"Unsupported element '{plan.Name.LocalName}'.",
                plan.Range.Start.Line + 1,
                plan.Range.Start.Column + 1));
            return null;
        }

        ConfigureCollectionBinding(element, plan, diagnostics);
        if (plan.ScopeName is { Length: > 0 } name && names is not null)
        {
            names[name] = element;
        }

        for (var i = 0; i < plan.Members.Length; i++)
        {
            ApplyPlanMember(element, plan.Members[i], resources, diagnostics);
        }

        if (plan.InlineEffect is { } effectPlan)
        {
            if (resources is null)
            {
                diagnostics.Add(new(
                    "XAML020",
                    "An inline EffectSet requires a mutable UiResourceCatalog in the cold loader or the generated XAML path.",
                    effectPlan.Range.Start.Line + 1,
                    effectPlan.Range.Start.Column + 1));
            }
            else if (TryMaterializeEffect(effectPlan, diagnostics, out var effect))
            {
                resources.Set(effect.Set.Resource.Value, effect);
                element.EffectSet = effect.Set;
            }
        }
        else
        {
            ApplyImplicitEffect(source, plan, element, resources, diagnostics);
        }

        for (var i = 0; i < plan.Children.Length; i++)
        {
            var child = Materialize(source, plan.Children[i], factory, resources, diagnostics, names);
            if (child is not null && !TryAttachChild(element, child))
            {
                diagnostics.Add(new(
                    "XAML003",
                    $"Element '{element.TypeName}' does not accept child '{child.TypeName}'.",
                    plan.Children[i].Range.Start.Line + 1,
                    plan.Children[i].Range.Start.Column + 1));
            }
        }

        if (!plan.TextSpans.IsDefaultOrEmpty)
        {
            if (element is not RichTextBlock richText)
            {
                diagnostics.Add(new(
                    "XAML040",
                    $"Element '{plan.Name.LocalName}' does not accept Span elements.",
                    plan.Range.Start.Line + 1,
                    plan.Range.Start.Column + 1));
            }
            else
            {
                var spans = new Delta.XAML.UiTextSpan[plan.TextSpans.Length];
                var valid = true;
                for (var i = 0; i < plan.TextSpans.Length; i++)
                {
                    var span = plan.TextSpans[i];
                    if (!TryColor(span.Color, out var color))
                    {
                        diagnostics.Add(new("XAML041", $"Invalid Span Foreground '{span.Color}'.", span.Range.Start.Line + 1, span.Range.Start.Column + 1));
                        valid = false;
                        continue;
                    }

                    spans[i] = new(
                        span.Text,
                        span.FontKey,
                        span.FontSize,
                        new(color.R, color.G, color.B, color.A),
                        span.Command == Guid.Empty ? Delta.XAML.UiCommandId.Empty : new(span.Command),
                        span.Argument,
                        span.Decorations);
                }

                if (valid)
                {
                    richText.Spans = spans;
                }
            }
        }

        return element;
    }

    private static void ConfigureCollectionBinding(
        UiElement element,
        XamlObjectPlan plan,
        List<XamlMaterializerDiagnostic> diagnostics)
    {
        XamlMemberPlan? sourceMember = null;
        string? templateKey = null;
        string? selectorKey = null;
        var start = 0;
        var count = -1;
        var extent = 24f;
        for (var index = 0; index < plan.Members.Length; index++)
        {
            var member = plan.Members[index];
            switch (member.Name)
            {
                case "ItemsSource": sourceMember = member; break;
                case "ItemTemplate" when member.Value.Kind == XamlValueKind.String:
                    templateKey = member.Value.Literal.CanonicalText;
                    break;
                case "ItemTemplateSelector" when member.Value.Kind == XamlValueKind.String:
                    selectorKey = member.Value.Literal.CanonicalText;
                    break;
                case "VirtualizationStart" when int.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart):
                    start = parsedStart;
                    break;
                case "VirtualizationCount" when int.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount):
                    count = parsedCount;
                    break;
                case "ItemExtent" when float.TryParse(member.Value.Literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExtent):
                    extent = parsedExtent;
                    break;
            }
        }

        if (sourceMember is null)
        {
            return;
        }

        var source = sourceMember.Value;

        if (element is not (ItemsControl or CollectionView or Picker))
        {
            diagnostics.Add(new("XAML020", "Property 'ItemsSource' is supported only on ItemsControl, CollectionView and Picker in the cold path.", source.Range.Start.Line + 1, source.Range.Start.Column + 1));
            return;
        }

        if (source.Value.Kind != XamlValueKind.ItemsSource || source.Value.Binding.SourceKind != XamlBindingSourceKind.Context)
        {
            diagnostics.Add(new("XAML008", "Cold collection loading supports a Context ItemsSource binding.", source.Range.Start.Line + 1, source.Range.Start.Column + 1));
            return;
        }

        if (start < 0 || count == 0 || count < -1 || !float.IsFinite(extent) || extent <= 0)
        {
            diagnostics.Add(new("XAML003", "VirtualizationStart must be non-negative, VirtualizationCount positive when specified and ItemExtent finite and positive.", source.Range.Start.Line + 1, source.Range.Start.Column + 1));
            return;
        }

        element.AddCollectionBindingSpec(new(
            source.Value.Binding.Path,
            ToRetainedBindingMode(source.Value.Binding.Mode),
            templateKey,
            selectorKey,
            start,
            count,
            extent,
            element is Picker));
    }

    private static void ApplyImplicitEffect(
        SourceId source,
        XamlObjectPlan plan,
        UiElement element,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics)
    {
        if (element.EffectSet.IsValid)
        {
            return;
        }

        UiEffectResource effect;
        if (HasBorderThickness(element.BorderThickness))
        {
            var thickness = element.BorderThickness;
            effect = UiEffectResource.CreateVisualStroke(
                XamlImplicitEffectIdentity.Create(source, plan),
                ToColor(element.BorderColor),
                new float4(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom),
                element.BorderWidthUnits);
        }
        else if (element.BorderWidth > 0)
        {
            effect = UiEffectResource.CreateVisualStroke(
                XamlImplicitEffectIdentity.Create(source, plan),
                ToColor(element.BorderColor),
                element.BorderWidth,
                element.BorderWidthUnits);
        }
        else if (element is TextBlock { StrokeWidth: > 0 } text)
        {
            effect = UiEffectResource.CreateTextStroke(
                XamlImplicitEffectIdentity.Create(source, plan, "text"),
                ToColor(text.StrokeColor),
                text.StrokeWidth);
        }
        else
        {
            return;
        }

        if (resources is null)
        {
            diagnostics.Add(new(
                "XAML020",
                "Stroke convenience properties require a mutable UiResourceCatalog in the cold loader or the generated XAML path.",
                plan.Range.Start.Line + 1,
                plan.Range.Start.Column + 1));
            return;
        }

        resources.Set(effect.Set.Resource.Value, effect);
        element.EffectSet = effect.Set;
    }

    private static float4 ToColor(UiColor color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    private static bool HasBorderThickness(UiThickness thickness) =>
        thickness.Left > 0 || thickness.Top > 0 || thickness.Right > 0 || thickness.Bottom > 0;

    private static bool TryMaterializeEffect(
        XamlEffectPlan plan,
        List<XamlMaterializerDiagnostic> diagnostics,
        out UiEffectResource effect)
    {
        for (var layerIndex = 0; layerIndex < plan.Layers.Length; layerIndex++)
        {
            var members = plan.Layers[layerIndex].Members;
            for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                if (members[memberIndex].Value.Kind == XamlValueKind.Binding)
                {
                    diagnostics.Add(new(
                        "XAML020",
                        "Effect layer bindings require the generated XAML path.",
                        members[memberIndex].Range.Start.Line + 1,
                        members[memberIndex].Range.Start.Column + 1));
                    effect = default;
                    return false;
                }
            }
        }

        var stroke = MaterializeLayer(plan, XamlEffectLayerKind.Stroke);
        var outerShadow = MaterializeLayer(plan, XamlEffectLayerKind.OuterShadow);
        var innerShadow = MaterializeLayer(plan, XamlEffectLayerKind.InnerShadow);
        var outerGlow = MaterializeLayer(plan, XamlEffectLayerKind.OuterGlow);
        var innerGlow = MaterializeLayer(plan, XamlEffectLayerKind.InnerGlow);
        float4? outsets = null;
        if (plan.Outsets is { } outsetsPlan &&
            ThicknessLiteralParser.TryParse(outsetsPlan.Literal.CanonicalText, out var parsedOutsets))
        {
            outsets = new(parsedOutsets.Left, parsedOutsets.Top, parsedOutsets.Right, parsedOutsets.Bottom);
        }

        try
        {
            effect = plan.Target == UiEffectTarget.Text
                ? Delta.XAML.UiEffects.CreateText(
                    plan.Resource,
                    EffectCapabilities(plan),
                    stroke,
                    outerShadow,
                    innerShadow,
                    outerGlow,
                    innerGlow,
                    plan.Units,
                    plan.Quality,
                    plan.CachedMask,
                    outsets)
                : Delta.XAML.UiEffects.CreateVisual(
                    plan.Resource,
                    EffectCapabilities(plan),
                    stroke,
                    outerShadow,
                    innerShadow,
                    outerGlow,
                    innerGlow,
                    plan.Units,
                    plan.Quality,
                    plan.CachedMask,
                    outsets);
            return true;
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new(
                "XAML047",
                exception.Message,
                plan.Range.Start.Line + 1,
                plan.Range.Start.Column + 1));
            effect = default;
            return false;
        }
    }

    private static UiEffectLayer MaterializeLayer(XamlEffectPlan plan, XamlEffectLayerKind kind)
    {
        XamlEffectLayerPlan? layer = null;
        for (var i = 0; i < plan.Layers.Length; i++)
        {
            if (plan.Layers[i].Kind == kind)
            {
                layer = plan.Layers[i];
                break;
            }
        }

        if (layer is null)
        {
            return default;
        }

        var color = default(float4);
        var offset = default(float2);
        var width = 0f;
        var radius = 0f;
        var spread = 0f;
        var intensity = 1f;
        for (var i = 0; i < layer.Members.Length; i++)
        {
            var member = layer.Members[i];
            var value = member.Value.Literal.CanonicalText;
            switch (member.Name)
            {
                case "Color": TryEffectColor(value, out color); break;
                case "Offset": TryEffectOffset(value, out offset); break;
                case "Width": float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out width); break;
                case "Radius": float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out radius); break;
                case "Spread": float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out spread); break;
                case "Intensity": float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out intensity); break;
            }
        }

        return new(color, offset, width, radius, spread, intensity);
    }

    private static bool TryEffectColor(string value, out float4 color)
    {
        if (!TryColor(value, out var parsed))
        {
            color = default;
            return false;
        }

        color = new(parsed.R / 255f, parsed.G / 255f, parsed.B / 255f, parsed.A / 255f);
        return true;
    }

    private static UiEffectCapabilities EffectCapabilities(XamlEffectPlan plan)
    {
        var capabilities = UiEffectCapabilities.None;
        for (var i = 0; i < plan.Layers.Length; i++)
        {
            capabilities |= plan.Layers[i].Kind switch
            {
                XamlEffectLayerKind.Stroke => UiEffectCapabilities.Stroke,
                XamlEffectLayerKind.OuterShadow => UiEffectCapabilities.OuterShadow,
                XamlEffectLayerKind.InnerShadow => UiEffectCapabilities.InnerShadow,
                XamlEffectLayerKind.OuterGlow => UiEffectCapabilities.OuterGlow,
                XamlEffectLayerKind.InnerGlow => UiEffectCapabilities.InnerGlow,
                _ => UiEffectCapabilities.None,
            };
        }

        return capabilities;
    }

    private static bool TryEffectOffset(string value, out float2 offset)
    {
        var separator = value.IndexOf(',', StringComparison.Ordinal);
        if (separator <= 0 ||
            !float.TryParse(value.AsSpan(0, separator), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(value.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            offset = default;
            return false;
        }

        offset = new(x, y);
        return true;
    }

    private static void ApplyPlanMember(
        UiElement element,
        XamlMemberPlan member,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics)
    {
        var line = member.Range.Start.Line + 1;
        var column = member.Range.Start.Column + 1;
        switch (member.Value.Kind)
        {
            case XamlValueKind.Binding:
                element.AddBindingSpec(new(
                    member.Name,
                    member.Value.Binding.Path,
                    ToRetainedBindingMode(member.Value.Binding.Mode),
                    member.Value.Binding.ConverterKey,
                    member.Value.Binding.StringFormat,
                    member.Value.Binding.CultureName,
                    member.Value.Binding.SourceKind switch
                    {
                        XamlBindingSourceKind.Context => Delta.XAML.UiBindingSourceKind.Context,
                        XamlBindingSourceKind.Self => Delta.XAML.UiBindingSourceKind.Self,
                        XamlBindingSourceKind.TemplateOwner => Delta.XAML.UiBindingSourceKind.TemplateOwner,
                        XamlBindingSourceKind.Name => Delta.XAML.UiBindingSourceKind.Name,
                        XamlBindingSourceKind.Ancestor => Delta.XAML.UiBindingSourceKind.Ancestor,
                        _ => Delta.XAML.UiBindingSourceKind.Context,
                    },
                    member.Value.Binding.SourceArgument));
                return;
            case XamlValueKind.MultiBinding:
                element.AddMultiBindingSpec(new(
                    member.Name,
                    member.Value.MultiBinding.Sources.Select(static source => new UiMultiBindingSourceSpec(
                        source.SourceKind switch
                        {
                            XamlBindingSourceKind.Context => Delta.XAML.UiBindingSourceKind.Context,
                            XamlBindingSourceKind.Self => Delta.XAML.UiBindingSourceKind.Self,
                            XamlBindingSourceKind.TemplateOwner => Delta.XAML.UiBindingSourceKind.TemplateOwner,
                            XamlBindingSourceKind.Name => Delta.XAML.UiBindingSourceKind.Name,
                            XamlBindingSourceKind.Ancestor => Delta.XAML.UiBindingSourceKind.Ancestor,
                            _ => Delta.XAML.UiBindingSourceKind.Context,
                        },
                        source.SourceArgument,
                        source.Path)).ToArray(),
                    member.Value.MultiBinding.FunctionKey,
                    member.Value.MultiBinding.StringFormat,
                    member.Value.MultiBinding.CultureName));
                return;
            case XamlValueKind.ItemsSource:
                if (element is not (ItemsControl or CollectionView or Picker))
                {
                    diagnostics.Add(new("XAML020", "Property 'ItemsSource' is supported only on ItemsControl, CollectionView and Picker in the cold path.", line, column));
                }
                return;
            case XamlValueKind.ResourceReference:
                ApplyResourceMember(element, member, resources, diagnostics, line, column);
                return;
            default:
                ApplyLiteral(element, member.Name, member.Value.Literal.CanonicalText, line, diagnostics, resources);
                return;
        }
    }

    private static void ApplyResourceMember(
        UiElement element,
        XamlMemberPlan member,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics,
        int line,
        int column)
    {
        var resource = member.Value.Resource;
        if (resources is null)
        {
            diagnostics.Add(new("XAML004", $"Resource reference '{resource.Key}' requires a resource store.", line, column));
            return;
        }

        var reference = new UiResourceReference(resource.Key, resource.Id.Value);
        if (!resources.TryResolve(reference, out var resolved, out var resourceDiagnostic))
        {
            diagnostics.Add(new("XAML006", resourceDiagnostic ?? $"Resource '{resource.Key}' was not resolved.", line, column));
            return;
        }

        if (member.Name == "EffectSet" && resolved is UiEffectResource effectResource)
        {
            resolved = effectResource.Set;
        }

        if (!IsResourceValueCompatible(member.Name, resolved))
        {
            diagnostics.Add(new("XAML010", $"Resource '{resource.Key}' is not compatible with property '{member.Name}' on '{element.TypeName}'.", line, column));
            return;
        }

        if (resource.IsDynamic)
        {
            element.SetStyleResource(member.Name, resources, reference, InvalidationFor(member.Name));
        }
        else
        {
            element.SetStyle(member.Name, resolved, InvalidationFor(member.Name));
        }
    }

    private static UiBindingMode ToRetainedBindingMode(Delta.XAML.UiBindingMode mode) => mode switch
    {
        Delta.XAML.UiBindingMode.OneTime => UiBindingMode.OneTime,
        Delta.XAML.UiBindingMode.OneWay => UiBindingMode.OneWay,
        Delta.XAML.UiBindingMode.TwoWay => UiBindingMode.TwoWay,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported binding mode."),
    };

    private static bool TryAttachChild(UiElement parent, UiElement child)
    {
        switch (parent)
        {
            case Panel panel:
                panel.Add(child);
                return true;
            case StackPanel stackPanel:
                stackPanel.Add(child);
                return true;
            case Grid grid:
                grid.Add(child);
                return true;
            case Border border:
                border.Add(child);
                return true;
            case ContentControl content:
                content.Content = child;
                return true;
            case Button button:
                button.Content = child;
                return true;
            case ToggleButton toggle:
                toggle.Content = child;
                return true;
            case ScrollViewer scrollViewer:
                scrollViewer.Content = child;
                return true;
            case Overlay overlay:
                overlay.Add(child);
                return true;
            case ItemsControl items:
                items.Add(child);
                return true;
            case CollectionView collection when collection.Children.Count > 0 &&
                collection.Children[0] is ScrollViewer { Content: ItemsControl itemsHost }:
                itemsHost.Add(child);
                return true;
            case Menu menu when menu.Children.Count > 0 && menu.Children[0] is ItemsControl itemsHost:
                itemsHost.Add(child);
                return true;
            case TabView tabs when tabs.Children.Count > 1 &&
                tabs.Children[1] is ContentControl { Content: null } contentHost:
                contentHost.Content = child;
                return true;
            default:
                return false;
        }
    }

    private static bool IsGeneratedOnlyProperty(string name) => name is
        "ItemsSource" or "ItemTemplate" or "ItemTemplateSelector" or
        "VirtualizationStart" or "VirtualizationCount" or "ItemExtent";

    private static void ApplyLiteral(UiElement e, string name, string value, int line, List<XamlMaterializerDiagnostic> d, UiResourceStore? resources)
    {
        if (TryApplyAttachedProperty(e, name, value, line, d))
        {
            return;
        }

        if (IsGeneratedOnlyProperty(name))
        {
            return;
        }

        if (!SupportsProperty(e, name))
        {
            d.Add(new("XAML003", $"Unsupported property '{name}' on '{e.TypeName}'.", line, 1));
            return;
        }

        switch (name)
        {
            case "Width" when TryDimension(value, out var w): e.Width = w; break;
            case "Width": d.Add(new("XAML003", $"Invalid Width '{value}'. Expected NaN or a finite non-negative value.", line, 1)); break;
            case "Height" when TryDimension(value, out var h): e.Height = h; break;
            case "Height": d.Add(new("XAML003", $"Invalid Height '{value}'. Expected NaN or a finite non-negative value.", line, 1)); break;
            case "Margin" when TryThickness(value, out var margin): e.Margin = margin; break;
            case "Margin": d.Add(new("XAML003", $"Invalid Margin '{value}'. Expected one, two or four finite values.", line, 1)); break;
            case "HorizontalAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiHorizontalAlignment horizontalAlignment) && horizontalAlignment != Delta.XAML.UiHorizontalAlignment.Unknown:
                e.HorizontalAlignment = horizontalAlignment;
                break;
            case "VerticalAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiVerticalAlignment verticalAlignment) && verticalAlignment != Delta.XAML.UiVerticalAlignment.Unknown:
                e.VerticalAlignment = verticalAlignment;
                break;
            case "Background" when TryColor(value, out var color): e.Background = color; break;
            case "BorderColor" when TryColor(value, out var borderColor): e.BorderColor = borderColor; break;
            case "BorderWidth" when TryFloat(value, out var borderWidth): e.BorderWidth = borderWidth; break;
            case "BorderThickness" when TryThickness(value, out var borderThickness): e.BorderThickness = borderThickness; break;
            case "BorderThickness": d.Add(new("XAML003", $"Invalid BorderThickness '{value}'. Expected one, two or four finite non-negative values.", line, 1)); break;
            case "BorderWidthUnits" when Enum.TryParse(value, true, out Delta.XAML.Contract.PaintUnits borderWidthUnits) &&
                borderWidthUnits is Delta.XAML.Contract.PaintUnits.Logical or Delta.XAML.Contract.PaintUnits.Device:
                e.BorderWidthUnits = borderWidthUnits;
                break;
            case "BorderWidthUnits": d.Add(new("XAML003", $"Invalid BorderWidthUnits '{value}'. Expected Logical or Device.", line, 1)); break;
            case "CornerRadius" when TryCornerRadii(value, out var cornerRadii): e.CornerRadius = cornerRadii; break;
            case "CornerRadius": d.Add(new("XAML003", $"Invalid CornerRadius '{value}'. Expected one value or four comma-separated values.", line, 1)); break;
            case "BackgroundBrush" when TryBrush(value, out var brush):
                if (!UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.BackgroundBrush, new UiValue(brush, UiValueSource.Local, InvalidationFor(name))))
                {
                    d.Add(new("XAML003", $"Invalid BackgroundBrush '{value}' on '{e.TypeName}'.", line, 1));
                }

                break;
            case "BackgroundBrush": d.Add(new("XAML003", $"Invalid BackgroundBrush '{value}'.", line, 1)); break;
            case "ForegroundBrush" when TryBrush(value, out var foregroundBrush):
                if (!UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.ForegroundBrush, new UiValue(foregroundBrush, UiValueSource.Local, InvalidationFor(name))))
                {
                    d.Add(new("XAML003", $"Invalid ForegroundBrush '{value}' on '{e.TypeName}'.", line, 1));
                }

                break;
            case "ForegroundBrush": d.Add(new("XAML003", $"Invalid ForegroundBrush '{value}'.", line, 1)); break;
            case "EffectSet": d.Add(new("XAML003", "EffectSet must be supplied through a typed StaticResource or DynamicResource.", line, 1)); break;
            case "BlendMode" when Enum.TryParse(value, true, out Delta.XAML.Contract.UiBlendMode blendMode) && blendMode is
                Delta.XAML.Contract.UiBlendMode.Opaque or Delta.XAML.Contract.UiBlendMode.Alpha or
                Delta.XAML.Contract.UiBlendMode.PremultipliedAlpha or Delta.XAML.Contract.UiBlendMode.Additive or
                Delta.XAML.Contract.UiBlendMode.Multiply:
                e.BlendMode = blendMode;
                break;
            case "BlendMode": d.Add(new("XAML003", $"Invalid BlendMode '{value}'. Expected Opaque, Alpha, PremultipliedAlpha, Additive or Multiply.", line, 1)); break;
            case "Orientation" when e is StackPanel s && Enum.TryParse(value, true, out UiOrientation orientation): s.Orientation = orientation; break;
            case "Columns" when e is Grid grid && TryGridLengths(value, out var columns): grid.SetColumns(columns); break;
            case "Rows" when e is Grid grid && TryGridLengths(value, out var rows): grid.SetRows(rows); break;
            case "Text": e.SetLocal("Text", value, InvalidationFor("Text")); break;
            case "FontKey": e.SetLocal("FontKey", value, InvalidationFor("FontKey")); break;
            case "FontSize" when TryFloat(value, out var size): e.SetLocal("FontSize", size, InvalidationFor("FontSize")); break;
            case "HorizontalTextAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiTextHorizontalAlignment horizontal) && horizontal != Delta.XAML.UiTextHorizontalAlignment.Unknown: e.SetLocal(name, horizontal, InvalidationFor(name)); break;
            case "VerticalTextAlignment" when Enum.TryParse(value, true, out Delta.XAML.UiTextVerticalAlignment vertical) && vertical != Delta.XAML.UiTextVerticalAlignment.Unknown: e.SetLocal(name, vertical, InvalidationFor(name)); break;
            case "TextWrapping" when Enum.TryParse(value, true, out Delta.XAML.UiTextWrapping wrapping) && wrapping != Delta.XAML.UiTextWrapping.Unknown: e.SetLocal(name, wrapping, InvalidationFor(name)); break;
            case "TextTrimming" when Enum.TryParse(value, true, out Delta.XAML.UiTextTrimming trimming) && trimming != Delta.XAML.UiTextTrimming.Unknown: e.SetLocal(name, trimming, InvalidationFor(name)); break;
            case "MaxLines" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLines) && maxLines >= 0: e.SetLocal(name, maxLines, InvalidationFor(name)); break;
            case "LineHeight" when TryFloat(value, out var lineHeight) && lineHeight >= 0: e.SetLocal(name, lineHeight, InvalidationFor(name)); break;
            case "FontWeight" when Enum.TryParse(value, true, out Delta.XAML.UiFontWeight weight) && weight != Delta.XAML.UiFontWeight.Unknown: e.SetLocal(name, weight, InvalidationFor(name)); break;
            case "FontStyle" when Enum.TryParse(value, true, out Delta.XAML.UiFontStyle style) && style != Delta.XAML.UiFontStyle.Unknown: e.SetLocal(name, style, InvalidationFor(name)); break;
            case "TextDecorations" when TryTextDecorations(value, out var decorations): e.SetLocal(name, decorations, InvalidationFor(name)); break;
            case "PlaceholderText" when e is TextBox or NumericEditor: e.SetLocal(name, value, InvalidationFor(name)); break;
            case "IsReadOnly" when e is TextBox or NumericEditor && bool.TryParse(value, out var isReadOnly): e.SetLocal(name, isReadOnly, InvalidationFor(name)); break;
            case "AcceptsReturn" when e is TextBox or NumericEditor && bool.TryParse(value, out var acceptsReturn): e.SetLocal(name, acceptsReturn, InvalidationFor(name)); break;
            case "MaxLength" when e is TextBox or NumericEditor && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLength) && maxLength >= 0: e.SetLocal(name, maxLength, InvalidationFor(name)); break;
            case "Minimum" when e is NumericEditor numeric && TryFloat(value, out var minimum): numeric.Min = minimum; break;
            case "Maximum" when e is NumericEditor numeric && TryFloat(value, out var maximum): numeric.Max = maximum; break;
            case "Value" when e is NumericEditor numeric && TryFloat(value, out var numericValue): numeric.Initialize(numericValue); break;
            case "Minimum" when e is Slider slider && TryDouble(value, out var sliderMinimum): slider.Minimum = sliderMinimum; break;
            case "Maximum" when e is Slider slider && TryDouble(value, out var sliderMaximum): slider.Maximum = sliderMaximum; break;
            case "Value" when e is Slider slider && TryDouble(value, out var sliderValue): slider.Value = sliderValue; break;
            case "Step" when e is Slider slider && TryDouble(value, out var sliderStep): slider.Step = sliderStep; break;
            case "Orientation" when e is Slider slider && Enum.TryParse(value, true, out UiOrientation sliderOrientation): slider.Orientation = sliderOrientation; break;
            case "Source" when e is Image image && Guid.TryParse(value, out var source): image.Source = source; break;
            case "Tint" when e is Image image && TryColor(value, out var tint): image.Tint = tint; break;
            case "Stretch" when e is Image image && Enum.TryParse(value, true, out Delta.XAML.UiImageStretch stretch): image.Stretch = (byte)stretch; break;
            case "Placeholder" when e is Image image && Guid.TryParse(value, out var placeholder): image.Placeholder = placeholder; break;
            case "ErrorSource" when e is Image image && Guid.TryParse(value, out var errorSource): image.ErrorSource = errorSource; break;
            case "IsOpen" when e is Overlay overlay && bool.TryParse(value, out var isOpen): overlay.IsOpen = isOpen; break;
            case "SelectedIndex" when e is CollectionView collection && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var collectionIndex): collection.SelectedIndex = collectionIndex; break;
            case "SelectedIndex" when e is Picker picker && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pickerIndex): picker.SelectedIndex = pickerIndex; break;
            case "IsOpen" when e is Picker picker && bool.TryParse(value, out var pickerOpen): picker.IsOpen = pickerOpen; break;
            case "SelectedIndex" when e is TabView tabs && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex): tabs.SelectedIndex = tabIndex; break;
            case "Foreground" when TryColor(value, out var fg): e.SetLocal("Foreground", fg, InvalidationFor("Foreground")); break;
            case "StrokeColor" when TryColor(value, out var strokeColor): e.SetLocal("StrokeColor", strokeColor, InvalidationFor("StrokeColor")); break;
            case "StrokeWidth" when TryFloat(value, out var strokeWidth): e.SetLocal("StrokeWidth", strokeWidth, InvalidationFor("StrokeWidth")); break;
            case "ForegroundResource" when resources is not null: e.SetStyleResource("Foreground", resources, new(value), InvalidationFor("Foreground")); break;
            case "ForegroundResource": d.Add(new("XAML004", "ForegroundResource requires a resource store.", line, 1)); break;
            case "Padding" when TryThickness(value, out var padding): e.Padding = padding; break;
            case "Padding": d.Add(new("XAML003", $"Invalid Padding '{value}'. Expected one, two or four finite values.", line, 1)); break;
            case "StyleKey": e.StyleKey = value; break;
            case "Variant": e.Variant = value; break;
            case "TemplateKey": e.TemplateKey = value; break;
            case "AutomationName": e.AutomationName = value; break;
            case "AutomationRole" when Enum.TryParse(value, true, out UiAutomationRole role): e.AutomationRole = role; break;
            case "IsEnabled" when bool.TryParse(value, out var enabled): e.IsEnabled = enabled; break;
            case "IsSelected" when bool.TryParse(value, out var selected): e.IsSelected = selected; break;
            case "Gestures" when TryGestureKind(value, out var gestures):
                UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.Gestures, new UiValue(gestures, UiValueSource.Local, InvalidationFor(name)));
                break;
            case "Command" when Guid.TryParse(value, out var command):
                UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.Command, new UiValue(new Delta.XAML.UiCommandId(command), UiValueSource.Local, InvalidationFor(name)));
                break;
            case "CommandKey" when TryKeyGesture(value, out var commandKey):
                UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.CommandKey, new UiValue(commandKey, UiValueSource.Local, InvalidationFor(name)));
                break;
            case "IsFocusScope" when bool.TryParse(value, out var focusScope):
                UiDescriptorCatalog.TrySetProperty(e, UiPropertyKey.IsFocusScope, new UiValue(focusScope, UiValueSource.Local, InvalidationFor(name)));
                break;
            default: d.Add(new("XAML003", $"Unsupported property '{name}'.", line, 1)); break;
        }
    }

    private static bool TryApplyAttachedProperty(UiElement element, string name, string value, int line, List<XamlMaterializerDiagnostic> diagnostics)
    {
        if (!name.StartsWith("Grid.", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            diagnostics.Add(new("XAML003", $"Invalid attached property '{name}' value '{value}'.", line, 1));
            return true;
        }

        switch (name)
        {
            case "Grid.Row": element.SetGridRow(parsed); return true;
            case "Grid.Column": element.SetGridColumn(parsed); return true;
            case "Grid.RowSpan" when parsed > 0: element.SetGridRowSpan(parsed); return true;
            case "Grid.ColumnSpan" when parsed > 0: element.SetGridColumnSpan(parsed); return true;
            default:
                diagnostics.Add(new("XAML003", $"Unsupported attached property '{name}'.", line, 1));
                return true;
        }
    }

    private static bool SupportsProperty(UiElement element, string name)
    {
        if (name is "Width" or "Height" or "Margin" or "HorizontalAlignment" or "VerticalAlignment" or "Background" or "BorderColor" or "BorderWidth" or "BorderThickness" or "BorderWidthUnits" or "CornerRadius" or "Padding" or
            "StyleKey" or "Variant" or "TemplateKey" or "AutomationName" or "AutomationRole" or
            "IsEnabled" or "IsSelected" or "BackgroundBrush" or "EffectSet" or "BlendMode")
        {
            return true;
        }

        if (element is TextBlock or TextBox or NumericEditor &&
            name is "Text" or "FontKey" or "FontSize" or "Foreground" or "ForegroundBrush" or "ForegroundResource" or "StrokeColor" or "StrokeWidth" or
            "HorizontalTextAlignment" or "VerticalTextAlignment" or "TextWrapping" or "TextTrimming" or "MaxLines" or "LineHeight" or
            "FontWeight" or "FontStyle" or "TextDecorations")
        {
            return true;
        }

        if (element is TextBox or NumericEditor && name is "PlaceholderText" or "IsReadOnly" or "AcceptsReturn" or "MaxLength")
        {
            return true;
        }

        return (element is StackPanel && name == "Orientation") ||
               (element is Grid && name is "Columns" or "Rows") ||
               (element is NumericEditor && name is "Minimum" or "Maximum" or "Value") ||
               (element is Slider && name is "Minimum" or "Maximum" or "Value" or "Step" or "Orientation") ||
               (element is Image && name is "Source" or "Tint" or "Stretch" or "Placeholder" or "ErrorSource") ||
               (element is Overlay && name == "IsOpen") ||
               (element is CollectionView or Picker or TabView && name == "SelectedIndex") ||
               (element is Picker && name == "IsOpen") ||
               name is "Gestures" or "Command" or "CommandKey" or "IsFocusScope";
    }

    private static bool IsResourceValueCompatible(string property, object? value) => property switch
    {
        "Background" or "BorderColor" or "Foreground" or "StrokeColor" => value is UiColor or Delta.XAML.UiColor,
        "BorderWidth" or "StrokeWidth" => value is
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "BorderThickness" => value is UiThickness or Delta.XAML.UiThickness,
        "BorderWidthUnits" => value is Delta.XAML.Contract.PaintUnits,
        "CornerRadius" => value is Delta.XAML.UiCornerRadii,
        "EffectSet" => value is UiEffectSet,
        "BlendMode" => value is Delta.XAML.Contract.UiBlendMode,
        "BackgroundBrush" or "ForegroundBrush" => value is Delta.XAML.UiBrush,
        "Padding" or "Margin" => value is UiThickness or Delta.XAML.UiThickness,
        "Width" or "Height" or "FontSize" or "Minimum" or "Maximum" or "Value" => value is
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "Step" => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "Source" or "Placeholder" or "ErrorSource" => value is UiResourceId or Delta.XAML.Contract.UiResourceId,
        "Tint" => value is UiColor or Delta.XAML.UiColor,
        "Stretch" => value is Delta.XAML.UiImageStretch,
        "SelectedIndex" => value is int,
        "IsOpen" => value is bool,
        "Gestures" => value is Delta.XAML.UiGestureKind,
        "Command" => value is Delta.XAML.UiCommandId,
        "CommandKey" => value is Delta.XAML.UiKeyGesture,
        "IsFocusScope" => value is bool,
        "IsEnabled" or "IsSelected" or "IsReadOnly" or "AcceptsReturn" => value is bool,
        "MaxLines" or "MaxLength" => value is int or byte or sbyte or short or ushort or uint,
        "HorizontalTextAlignment" => value is Delta.XAML.UiTextHorizontalAlignment,
        "VerticalTextAlignment" => value is Delta.XAML.UiTextVerticalAlignment,
        "HorizontalAlignment" => value is Delta.XAML.UiHorizontalAlignment,
        "VerticalAlignment" => value is Delta.XAML.UiVerticalAlignment,
        "TextWrapping" => value is Delta.XAML.UiTextWrapping,
        "TextTrimming" => value is Delta.XAML.UiTextTrimming,
        "FontWeight" => value is Delta.XAML.UiFontWeight,
        "FontStyle" => value is Delta.XAML.UiFontStyle,
        "TextDecorations" => value is Delta.XAML.UiTextDecorations,
        "LineHeight" => value is float or double or int,
        "Text" or "FontKey" or "StyleKey" or "Variant" or "TemplateKey" or "AutomationName" => value is string,
        _ => true,
    };
    private static UiDirtyFlags InvalidationFor(string name) => name switch
    {
        "Text" or "FontKey" or "FontSize" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "FontWeight" or "FontStyle" => UiDirtyFlags.Measure | UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "TextDecorations" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "TextWrapping" or "MaxLines" or "LineHeight" => UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "HorizontalTextAlignment" or "VerticalTextAlignment" or "TextTrimming" => UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "PlaceholderText" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "IsReadOnly" or "AcceptsReturn" or "MaxLength" => UiDirtyFlags.Visual,
        "Foreground" or "ForegroundBrush" or "StrokeColor" or "StrokeWidth" or "EffectSet" or "BlendMode" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "BorderColor" or "BorderWidth" or "BorderThickness" or "BorderWidthUnits" or "CornerRadius" => UiDirtyFlags.Visual,
        "Width" or "Height" or "Margin" or "Padding" or "Source" => UiDirtyFlags.Measure | UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "HorizontalAlignment" or "VerticalAlignment" => UiDirtyFlags.Arrange | UiDirtyFlags.Visual,
        "Minimum" or "Maximum" or "Step" or "Orientation" or "SelectedIndex" or "IsOpen" => UiDirtyFlags.Measure | UiDirtyFlags.Visual,
        "Stretch" or "Placeholder" or "ErrorSource" or "Tint" or "BackgroundBrush" => UiDirtyFlags.Visual,
        _ => UiDirtyFlags.Visual,
    };
    private static bool TryFloat(string value, out float result) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryDouble(string value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryTextDecorations(string value, out Delta.XAML.UiTextDecorations result)
    {
        result = Delta.XAML.UiTextDecorations.None;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (!Enum.TryParse(parts[i], true, out Delta.XAML.UiTextDecorations decoration) || decoration == Delta.XAML.UiTextDecorations.None)
            {
                result = default;
                return false;
            }

            result |= decoration;
        }

        return true;
    }
    private static bool TryCornerRadii(string value, out Delta.XAML.UiCornerRadii result)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        result = default;
        if (parts.Length == 1 && TryFloat(parts[0], out var uniform))
        {
            result = Delta.XAML.UiCornerRadii.Uniform(uniform);
            return result.IsFiniteNonNegative;
        }

        if (parts.Length != 4)
        {
            return false;
        }

        if (!TryFloat(parts[0], out var topLeft) ||
            !TryFloat(parts[1], out var topRight) ||
            !TryFloat(parts[2], out var bottomRight) ||
            !TryFloat(parts[3], out var bottomLeft))
        {
            return false;
        }

        result = new(topLeft, topRight, bottomRight, bottomLeft);
        return result.IsFiniteNonNegative;
    }
    private static bool TryGridLengths(string value, out GridLength[] result)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        result = new GridLength[parts.Length];
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.Equals(part, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                result[i] = GridLength.Auto;
            }
            else if (part.EndsWith('*'))
            {
                var weight = part.Length == 1 ? 1 : TryFloat(part[..^1], out var parsed) ? parsed : float.NaN;
                if (float.IsNaN(weight) || weight <= 0)
                {
                    result = Array.Empty<GridLength>();
                    return false;
                }

                result[i] = GridLength.Star(weight);
            }
            else if (TryFloat(part, out var pixels) && pixels >= 0)
            {
                result[i] = GridLength.Fixed(pixels);
            }
            else
            {
                result = Array.Empty<GridLength>();
                return false;
            }
        }

        return true;
    }

    private static bool TryBrush(string value, out Delta.XAML.UiBrush result)
    {
        result = default;
        if (TryColor(value, out var solid))
        {
            result = Delta.XAML.UiBrush.Solid(new Delta.XAML.UiColor(solid.R, solid.G, solid.B, solid.A));
            return true;
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || !Guid.TryParse(value[(separator + 1)..], out var resource) || resource == Guid.Empty)
        {
            return false;
        }

        var id = new Delta.XAML.Contract.UiResourceId(resource);
        result = value[..separator] switch
        {
            "LinearGradient" => Delta.XAML.UiBrush.LinearGradient(id),
            "RadialGradient" => Delta.XAML.UiBrush.RadialGradient(id),
            "Image" => Delta.XAML.UiBrush.Image(id),
            _ => default,
        };
        return result.Kind != Delta.XAML.UiBrushKind.None;
    }

    private static bool TryGestureKind(string value, out Delta.XAML.UiGestureKind result)
    {
        result = Delta.XAML.UiGestureKind.None;
        var normalized = value.Replace('|', ',');
        if (!Enum.TryParse(normalized, true, out result))
        {
            return false;
        }

        const Delta.XAML.UiGestureKind supported =
            Delta.XAML.UiGestureKind.Tap |
            Delta.XAML.UiGestureKind.MultipleTap |
            Delta.XAML.UiGestureKind.LongPress |
            Delta.XAML.UiGestureKind.Drag |
            Delta.XAML.UiGestureKind.Pan |
            Delta.XAML.UiGestureKind.Swipe |
            Delta.XAML.UiGestureKind.Pinch;
        return (result & ~supported) == 0;
    }

    private static bool TryKeyGesture(string value, out Delta.XAML.UiKeyGesture result)
    {
        result = default;
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
        {
            return false;
        }

        ulong modifiers = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            var modifier = parts[i] switch
            {
                "Shift" => UiModifierBits.Shift,
                "Control" or "Ctrl" => UiModifierBits.Control,
                "Alt" => UiModifierBits.Alt,
                "Super" or "Meta" => UiModifierBits.Super,
                "CapsLock" => UiModifierBits.CapsLock,
                "NumLock" => UiModifierBits.NumLock,
                _ => 0UL,
            };
            if (modifier == 0)
            {
                return false;
            }

            modifiers |= modifier;
        }

        result = new(new(key), new(modifiers));
        return true;
    }

    private static bool TryDimension(string value, out float result) =>
        TryFloat(value, out result) && ElementPlacementMixin.IsValidDimension(result);

    private static bool TryThickness(string value, out UiThickness result)
    {
        result = default;
        if (!ThicknessLiteralParser.TryParse(value, out var thickness))
        {
            return false;
        }

        result = new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
        return true;
    }
    private static bool TryColor(string value, out UiColor color)
    {
        color = default;
        if (value.Length != 7 && value.Length != 9 || value[0] != '#')
        {
            return false;
        }

        try
        {
            var offset = value.Length == 9 ? 3 : 1;
            var alpha = value.Length == 9 ? Convert.ToByte(value[1..3], 16) : (byte)255;
            color = new(
                Convert.ToByte(value[offset..(offset + 2)], 16),
                Convert.ToByte(value[(offset + 2)..(offset + 4)], 16),
                Convert.ToByte(value[(offset + 4)..(offset + 6)], 16),
                alpha);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
