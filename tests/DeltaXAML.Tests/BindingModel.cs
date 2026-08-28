using System.ComponentModel;

namespace DeltaXaml.Tests;

public sealed class BindingModel : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class GeneratedBindingConverters
{
    [Delta.XAML.UiXamlConverter("Upper")]
    public static string ToUpper(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToUpperInvariant();
    }

    [Delta.XAML.UiXamlConverter("Upper", Delta.XAML.UiXamlConverterDirection.Backward)]
    public static string ToSource(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToLowerInvariant();
    }
}
