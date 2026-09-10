using Delta.Text.Contract;
using Delta;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.UiLibraryDemo.Generated;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class UiLibraryDemoContent : IDisposable
{
    private readonly UiLibraryDemoArtifact _artifact;
    private readonly DemoModel _model = new();
    private readonly UiGrid _cards;
    private readonly GridOverlay _background;
    private readonly GridOverlay _preview;
    private int _columns;

    internal UiLibraryDemoContent(ITextService textService, UiFontCatalog fonts)
    {
        _artifact = new UiLibraryDemoArtifact(_model,
            textService ?? throw new ArgumentNullException(nameof(textService)),
            fonts ?? throw new ArgumentNullException(nameof(fonts)));
        UiLibraryDemoResources.Register(_artifact.Resources);
        _cards = Find<UiGrid>("Cards");
        _background = new(Find<UiCollectionView>("VerticalGridLines"), Find<UiCollectionView>("HorizontalGridLines"),
            _model.VerticalLines, _model.HorizontalLines, 32);
        _preview = new(Find<UiCollectionView>("PreviewVertical"), Find<UiCollectionView>("PreviewHorizontal"),
            _model.PreviewVertical, _model.PreviewHorizontal, 16);
        SetSlots("Tabs", 4, 1);
        SetSlots("Actions", 2, 2);
        SetSlots("Menus", 2, 1);
        SetSlots("Icons", 6, 2);
        SetSlots("Badges", 3, 1);
        SetSlots("Status", 3, 2);
        SetSlots("Markers", 1, 3);
        SetSlots("Colors", 6, 1);
    }

    internal UiDocument Document => _artifact.Document;

    internal UiResourceCatalog Resources => _artifact.Resources;

    internal void AdvanceFrame(float width, float height)
    {
        _model.PageWidth = Maths.Max(0, width - 26);
        _background.Resize(width, height);
        var columns = Maths.Max(1, (int)((width - 26) / 290));
        _preview.Resize((width - 26) / columns - 36, 130);
        if (_columns == columns)
        {
            return;
        }

        _columns = columns;
        var definitions = new UiGridLength[columns];
        Array.Fill(definitions, UiGridLength.Star(1));
        _cards.SetColumns(definitions);
        var row = 0;
        var column = 0;
        foreach (var card in _cards.Children)
        {
            var wide = card.AutomationName is "Panel Toolbar" or "Typography";
            if (wide && column != 0)
            {
                row++;
                column = 0;
            }

            card.SetAttachedValue(UiGridAttachedProperties.Row, row);
            card.SetAttachedValue(UiGridAttachedProperties.Column, column);
            card.SetAttachedValue(UiGridAttachedProperties.ColumnSpan, wide ? columns : 1);
            column += wide ? columns : 1;
            if (column == columns)
            {
                row++;
                column = 0;
            }
        }

        var rows = new UiGridLength[row + (column == 0 ? 0 : 1)];
        Array.Fill(rows, UiGridLength.Auto);
        _cards.SetRows(rows);
    }

    private void SetSlots(string name, int columns, int rows) =>
        Find<UiCollectionView>(name).ItemsHost.SetGridDimensions(columns, rows);

    internal void HandleInput(in UiInputEvent input) => Document.Dispatch(in input);

    public void Dispose() => _artifact.Dispose();

    private T Find<T>(string name)
        where T : UiElement
    {
        if (_artifact.TryFindName(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"UI library demo namescope entry '{name}' is missing or has the wrong type.");
    }
}
