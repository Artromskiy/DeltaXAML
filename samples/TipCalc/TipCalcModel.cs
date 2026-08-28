using System.ComponentModel;

namespace DeltaXaml.Samples.TipCalc;

/// <summary>Calculation model retained from the original MAUI TipCalc sample.</summary>
public sealed class TipCalcModel : INotifyPropertyChanged
{
    private double _subTotal;
    private double _postTaxTotal;
    private double _tipPercent;
    private double _tipAmount;
    private double _total;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double SubTotal
    {
        get => _subTotal;
        set
        {
            if (_subTotal.Equals(value))
            {
                return;
            }

            _subTotal = value;
            OnPropertyChanged(nameof(SubTotal));
            Recalculate();
        }
    }

    public double PostTaxTotal
    {
        get => _postTaxTotal;
        set
        {
            if (_postTaxTotal.Equals(value))
            {
                return;
            }

            _postTaxTotal = value;
            OnPropertyChanged(nameof(PostTaxTotal));
            Recalculate();
        }
    }

    public double TipPercent
    {
        get => _tipPercent;
        set
        {
            if (_tipPercent.Equals(value))
            {
                return;
            }

            _tipPercent = value;
            OnPropertyChanged(nameof(TipPercent));
            Recalculate();
        }
    }

    public double TipAmount
    {
        get => _tipAmount;
        private set
        {
            if (_tipAmount.Equals(value))
            {
                return;
            }

            _tipAmount = value;
            OnPropertyChanged(nameof(TipAmount));
        }
    }

    public double Total
    {
        get => _total;
        private set
        {
            if (_total.Equals(value))
            {
                return;
            }

            _total = value;
            OnPropertyChanged(nameof(Total));
        }
    }

    private void Recalculate()
    {
        TipAmount = Math.Round(TipPercent * SubTotal / 100, 2);
        Total = Math.Round(4 * (PostTaxTotal + TipAmount)) / 4;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
