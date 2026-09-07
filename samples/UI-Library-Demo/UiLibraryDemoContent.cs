using Delta.Text.Contract;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.UiLibraryDemo.Generated;

namespace DeltaXaml.Samples.UiLibraryDemo;

internal sealed class UiLibraryDemoContent : IDisposable
{
    private readonly UiLibraryDemoArtifact _artifact;
    private readonly UiItemsControl _verticalGridLines;
    private readonly UiItemsControl _horizontalGridLines;

    internal UiLibraryDemoContent(ITextService textService, UiFontCatalog fonts)
    {
        _artifact = new UiLibraryDemoArtifact(
            textService ?? throw new ArgumentNullException(nameof(textService)),
            fonts ?? throw new ArgumentNullException(nameof(fonts)));
        _verticalGridLines = Find<UiItemsControl>("VerticalGridLines");
        _horizontalGridLines = Find<UiItemsControl>("HorizontalGridLines");
    }

    internal UiDocument Document => _artifact.Document;

    internal void AdvanceFrame() => GridOverlay.Update(_verticalGridLines, _horizontalGridLines);

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
