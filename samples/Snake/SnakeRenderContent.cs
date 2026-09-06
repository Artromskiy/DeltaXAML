using Delta.Text.Contract;
using Delta.Render.UI;
using Delta.XAML;
using Delta.XAML.Contract;
using DeltaXaml.Samples.Snake.Generated;

namespace DeltaXaml.Samples.Snake;

internal sealed class SnakeRenderContent : IUiRenderHostContent
{
    private readonly SnakeGame _game;
    private readonly SnakeArtifact _page;
    private readonly SnakeView _view;

    internal SnakeRenderContent(
        (int Columns, int Rows) grid,
        ITextService textService,
        UiFontCatalog fonts)
    {
        ArgumentNullException.ThrowIfNull(textService);
        ArgumentNullException.ThrowIfNull(fonts);
        _game = new SnakeGame(grid.Columns, grid.Rows);
        _page = new SnakeArtifact(_game, textService, fonts);
        _view = new SnakeView(_page);
        _game.StartNewGame();
        _view.Render(_game);
    }

    public UiDocument Document => _page.Document;

    public void AdvanceFrame()
    {
        _game.Tick();
        _view.Render(_game);
    }

    public void HandleInput(in UiInputEvent input)
    {
        Document.Dispatch(in input);
        if (input.Kind != UiInputEventKind.Key ||
            input.Key.Kind != UiKeyEventKind.Down || input.Key.IsRepeat)
        {
            return;
        }

        switch (input.Key.PhysicalKey.Value)
        {
            case 82 or 26:
                _game.SetDirection(Direction.Up);
                break;
            case 81 or 22:
                _game.SetDirection(Direction.Down);
                break;
            case 80 or 4:
                _game.SetDirection(Direction.Left);
                break;
            case 79 or 7:
                _game.SetDirection(Direction.Right);
                break;
            case 44:
                _game.TogglePause();
                _view.Render(_game);
                break;
            case 40:
                _game.StartNewGame();
                _view.Render(_game);
                break;
        }
    }

    public void Dispose() => _page.Dispose();
}
