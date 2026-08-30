using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;

namespace SnakeAvalonia;

public partial class MainWindow : Window
{
    private readonly SnakeViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Focus();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _viewModel.HandleKey(e.Key);
    }

    private void OnRestartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.StartNewGame();
        Focus();
    }

    private void OnPauseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.TogglePause();
        Focus();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
    }
}
