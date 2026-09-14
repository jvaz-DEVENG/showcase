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
    /// <summary>
    /// Com ConverterParameter="inverter", aparece quando o texto esta VAZIO.
    /// E o que faz o texto-fantasma de uma caixa de busca sumir assim que a
    /// pessoa comeca a digitar.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var vazio = string.IsNullOrWhiteSpace(value as string);

        if (string.Equals(parameter as string, "inverter", StringComparison.OrdinalIgnoreCase))
            return vazio ? Visibility.Visible : Visibility.Collapsed;

        return vazio ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// True vira negrito. Serve para destacar a linha "em uso agora" na tabela de
/// DNS sem precisar de uma cor a mais na paleta.
/// </summary>
public sealed class BoolParaPesoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Semaforo do NAT e da latencia: verde, amarelo, vermelho, cinza.
///
/// Usa as MESMAS cores do badge de risco, de proposito. Um verde aqui e um
/// verde la precisam ser o mesmo verde, senao o usuario aprende duas escalas
/// de cor no mesmo aplicativo.
/// </summary>
public sealed class SemaforoParaCorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "verde" => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
            "amarelo" => new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)),
            "vermelho" => new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x4F)),
            _ => new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A))
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
