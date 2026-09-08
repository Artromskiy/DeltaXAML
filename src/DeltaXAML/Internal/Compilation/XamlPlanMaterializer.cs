using System.Globalization;
using Delta;
using DeltaXAML.Compiler;
using Delta.XAML.Contract;
using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;
namespace DeltaXAML.Internal;

internal readonly record struct XamlMaterializerDiagnostic(string Code, string Message, int Line, int Column);
internal sealed record XamlMaterializerResult(UiElement? Root, IReadOnlyList<XamlMaterializerDiagnostic> Diagnostics);
/// <summary>Cold materializer selected only by the explicit <c>IXamlLoader</c> API.</summary>
/// <remarks>It consumes the compiler's semantic plan and constructs canonical descriptor-backed elements; generated production artifacts bypass XML inflation.</remarks>
internal static class XamlPlanMaterializer
{
    internal static XamlMaterializerResult Read(
        XamlDocumentPlan plan,
        Func<string, string, UiElement?>? factory,
        UiResourceStore? resources)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var diagnostics = new List<XamlMaterializerDiagnostic>();
        if (plan.Root is null)
        {
            return new(null, diagnostics);
        }

        for (var effectIndex = 0; effectIndex < plan.EffectResources.Length; effectIndex++)
        {
            var effectPlan = plan.EffectResources[effectIndex];
            if (resources is null)
            {
                diagnostics.Add(new(
                    "XAML020",
                    "EffectSet resources require a mutable UiResourceCatalog in the cold loader or the generated XAML path.",
                    effectPlan.Range.Start.Line + 1,
                    effectPlan.Range.Start.Column + 1));
                continue;
            }

            if (TryMaterializeEffect(effectPlan, diagnostics, out var effect))
            {
                resources.Set(effect.Set.Resource.Value, effect);
                if (effectPlan.Key is { } key)
                {
                    resources.Set(key, new UiResourceReference(effect.Set.Resource.Value));
                }
            }
        }

        var root = Materialize(plan.Root, factory, resources, diagnostics);
        return new(root, diagnostics);
    }

    private static UiElement? Materialize(
        XamlObjectPlan plan,
        Func<string, string, UiElement?>? factory,
        UiResourceStore? resources,
        List<XamlMaterializerDiagnostic> diagnostics)
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

        for (var i = 0; i < plan.Children.Length; i++)
        {
            var child = Materialize(plan.Children[i], factory, resources, diagnostics);
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

        var strokeOrOutline = MaterializeLayer(plan, plan.Target == UiEffectTarget.Text
            ? XamlEffectLayerKind.Outline
            : XamlEffectLayerKind.Stroke);
        var outerShadow = MaterializeLayer(plan, XamlEffectLayerKind.OuterShadow);
        var insetShadow = MaterializeLayer(plan, XamlEffectLayerKind.InsetShadow);
        var glow = MaterializeLayer(plan, XamlEffectLayerKind.Glow);
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
                    strokeOrOutline,
                    outerShadow,
                    glow,
                    plan.Units,
                    plan.Quality,
                    plan.CachedMask,
                    outsets)
                : Delta.XAML.UiEffects.CreateVisual(
                    plan.Resource,
                    strokeOrOutline,
                    outerShadow,
                    insetShadow,
                    glow,
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
                if (member.Value.Binding.SourceKind != XamlBindingSourceKind.Context)
                {
                    var code = member.Value.Binding.SourceKind == XamlBindingSourceKind.TemplateOwner ? "XAML020" : "XAML008";
                    var message = member.Value.Binding.SourceKind == XamlBindingSourceKind.TemplateOwner
                        ? "TemplateBinding requires the generated XAML path."
                        : "Cold loading supports only Context bindings; use the generated relation binding path.";
                    diagnostics.Add(new(code, message, line, column));
                }
                else
                {
                    element.AddBindingSpec(new(
                        member.Name,
                        member.Value.Binding.Path,
                        ToRetainedBindingMode(member.Value.Binding.Mode),
                        member.Value.Binding.ConverterKey,
                        member.Value.Binding.StringFormat,
                        member.Value.Binding.CultureName));
                }

                return;
            case XamlValueKind.MultiBinding:
                diagnostics.Add(new("XAML020", "MultiBinding requires the generated XAML path.", line, column));
                return;
            case XamlValueKind.ItemsSource:
                diagnostics.Add(new("XAML020", "Property 'ItemsSource' requires the generated XAML path.", line, column));
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
            d.Add(new("XAML020", $"Property '{name}' requires the generated XAML path.", line, 1));
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
            case "EffectSet": d.Add(new("XAML003", "EffectSet must be supplied through a typed StaticResource or DynamicResource.", line, 1)); break;
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
            case "OutlineColor" when TryColor(value, out var outlineColor): e.SetLocal("OutlineColor", outlineColor, InvalidationFor("OutlineColor")); break;
            case "OutlineWidth" when TryFloat(value, out var outlineWidth): e.SetLocal("OutlineWidth", outlineWidth, InvalidationFor("OutlineWidth")); break;
            case "TextEffect" when Guid.TryParse(value, out var effect): e.SetLocal("TextEffect", new UiResourceId(effect), InvalidationFor("TextEffect")); break;
            case "ForegroundResource" when resources is not null: e.SetStyleResource("Foreground", resources, new(value), InvalidationFor("Foreground")); break;
            case "ForegroundResource": d.Add(new("XAML004", "ForegroundResource requires a resource store.", line, 1)); break;
            case "Padding" when TryThickness(value, out var padding): e.Padding = padding; break;
            case "Padding": d.Add(new("XAML003", $"Invalid Padding '{value}'. Expected one, two or four finite values.", line, 1)); break;
            case "StyleKey": e.StyleKey = value; break;
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
        if (name is "Width" or "Height" or "Margin" or "HorizontalAlignment" or "VerticalAlignment" or "Background" or "BorderColor" or "BorderWidth" or "BorderWidthUnits" or "CornerRadius" or "Padding" or
            "StyleKey" or "TemplateKey" or "AutomationName" or "AutomationRole" or
            "IsEnabled" or "IsSelected" or "BackgroundBrush" or "EffectSet")
        {
            return true;
        }

        if (element is TextBlock or TextBox or NumericEditor &&
            name is "Text" or "FontKey" or "FontSize" or "Foreground" or "ForegroundResource" or "OutlineColor" or "OutlineWidth" or "TextEffect" or
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
        "Background" or "BorderColor" or "Foreground" or "OutlineColor" => value is UiColor or Delta.XAML.UiColor,
        "BorderWidth" or "OutlineWidth" => value is
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
        "BorderWidthUnits" => value is Delta.XAML.Contract.PaintUnits,
        "CornerRadius" => value is Delta.XAML.UiCornerRadii,
        "TextEffect" => value is UiResourceId,
        "EffectSet" => value is UiEffectSet,
        "BackgroundBrush" => value is Delta.XAML.UiBrush,
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
        "Text" or "FontKey" or "StyleKey" or "TemplateKey" or "AutomationName" => value is string,
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
        "Foreground" or "OutlineColor" or "OutlineWidth" or "TextEffect" or "EffectSet" => UiDirtyFlags.Visual | UiDirtyFlags.Text,
        "BorderColor" or "BorderWidth" or "BorderWidthUnits" or "CornerRadius" => UiDirtyFlags.Visual,
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
