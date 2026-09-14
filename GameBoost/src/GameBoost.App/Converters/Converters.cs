using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GameBoost.Core.Modules;

namespace GameBoost.App.Converters;

/// <summary>Badge de risco: verde, amarelo, vermelho (secao 6 do spec).</summary>
public sealed class RiscoParaCorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var chave = (value as RiskLevel?) switch
        {
            RiskLevel.Alto => "RiscoAlto",
            RiskLevel.Medio => "RiscoMedio",
            _ => "RiscoBaixo"
        };

        return Application.Current?.TryFindResource(chave) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolParaVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visivel = value is true;
        if (parameter as string == "inverter")
            visivel = !visivel;

        return visivel ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverterBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Mostra o elemento apenas quando o texto existe e nao esta vazio.</summary>
public sealed class TextoParaVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
