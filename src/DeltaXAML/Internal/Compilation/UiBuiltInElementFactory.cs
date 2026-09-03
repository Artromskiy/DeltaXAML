namespace DeltaXAML.Internal;

/// <summary>Cold source-loader factory for the canonical built-in retained elements.</summary>
/// <remarks>
/// This is a name-based entry point only for <c>IXamlLoader</c>. Generated artifacts use
/// the same typed descriptor companions directly and never enter this factory.
/// Composite controls are assembled from retained nodes so the cold path does not create
/// and discard public wrapper objects.
/// </remarks>
internal static class UiBuiltInElementFactory
{
    internal static UiElement? TryCreate(string name) => name switch
    {
        "Panel" => UiPanelGenerated.Create(),
        "StackPanel" => UiStackPanelGenerated.Create(),
        "ItemsControl" => UiItemsControlGenerated.Create(),
        "Border" => UiBorderGenerated.Create(),
        "Grid" => UiGridGenerated.Create(),
        "ContentControl" => UiContentControlGenerated.Create(),
        "Button" => UiButtonGenerated.Create(),
        "ToggleButton" => UiToggleButtonGenerated.Create(),
        "TextBlock" => TextBlockGenerated.Create(),
        "TextBox" => TextBoxGenerated.Create(),
        "NumericEditor" => UiNumericEditorGenerated.Create(),
        "ScrollViewer" => UiScrollViewerGenerated.Create(),
        "Slider" => UiSliderGenerated.Create(),
        "Image" => UiImageGenerated.Create(),
        "Overlay" => UiOverlayGenerated.Create(),
        "RichTextBlock" => UiRichTextGenerated.Create(),
        "CollectionView" => CreateCollectionView(),
        "Picker" => CreatePicker(),
        "TabView" => CreateTabView(),
        "Menu" => CreateMenu(),
        _ => null,
    };

    private static CollectionView CreateCollectionView()
    {
        var view = UiCollectionViewGenerated.Create();
        var items = UiItemsControlGenerated.Create();
        var viewport = UiScrollViewerGenerated.Create();
        viewport.Content = items;
        view.Add(viewport);
        return view;
    }

    private static Picker CreatePicker()
    {
        var picker = UiPickerGenerated.Create();
        var header = UiButtonGenerated.Create();
        var overlay = UiOverlayGenerated.Create();
        var items = CreateCollectionView();
        overlay.IsOpen = false;
        overlay.Add(items);
        picker.Add(header);
        picker.Add(overlay);
        return picker;
    }

    private static TabView CreateTabView()
    {
        var tabs = UiTabViewGenerated.Create();
        tabs.Add(UiItemsControlGenerated.Create());
        tabs.Add(UiContentControlGenerated.Create());
        return tabs;
    }

    private static Menu CreateMenu()
    {
        var menu = UiMenuGenerated.Create();
        menu.Add(UiItemsControlGenerated.Create());
        return menu;
    }
}
