using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameBoost.App.Controls;

/// <summary>
/// Grafico dos ultimos 60 segundos (secao 5.5).
///
/// Desenha em OnRender em vez de montar uma Polyline com centenas de pontos:
/// a serie e trocada inteira a cada segundo, e recriar objetos visuais nesse
/// ritmo apareceria no orcamento de CPU da propria coleta.
/// </summary>
public sealed class MiniGrafico : Control
{
    public static readonly DependencyProperty ValoresProperty = DependencyProperty.Register(
        nameof(Valores), typeof(IReadOnlyList<double>), typeof(MiniGrafico),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximoProperty = DependencyProperty.Register(
        nameof(Maximo), typeof(double), typeof(MiniGrafico),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CorDaLinhaProperty = DependencyProperty.Register(
        nameof(CorDaLinha), typeof(Brush), typeof(MiniGrafico),
        new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo), typeof(string), typeof(MiniGrafico),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Valores
    {
        get => (IReadOnlyList<double>?)GetValue(ValoresProperty);
        set => SetValue(ValoresProperty, value);
    }

    public double Maximo
    {
        get => (double)GetValue(MaximoProperty);
        set => SetValue(MaximoProperty, value);
    }

    public Brush CorDaLinha
    {
        get => (Brush)GetValue(CorDaLinhaProperty);
        set => SetValue(CorDaLinhaProperty, value);
    }

    public string Rotulo
    {
        get => (string)GetValue(RotuloProperty);
        set => SetValue(RotuloProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var largura = ActualWidth;
        var altura = ActualHeight;

        if (largura <= 1 || altura <= 1)
            return;

        // Linhas de grade em 25, 50 e 75%.
        var grade = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        grade.Freeze();

        for (var i = 1; i < 4; i++)
        {
            var y = altura * i / 4.0;
            dc.DrawLine(grade, new Point(0, y), new Point(largura, y));
        }

        var valores = Valores;
        if (valores is null || valores.Count < 2)
        {
            DesenharRotulo(dc, "aguardando dados", largura, altura);
            return;
        }

        var maximo = Maximo <= 0 ? 1 : Maximo;
        var passo = largura / Math.Max(valores.Count - 1, 1);

        var linha = new Pen(CorDaLinha, 1.6) { LineJoin = PenLineJoin.Round };
        linha.Freeze();

        var geometria = new StreamGeometry();
        using (var ctx = geometria.Open())
        {
            var primeiro = new Point(0, Y(valores[0], maximo, altura));
            ctx.BeginFigure(primeiro, isFilled: false, isClosed: false);

            for (var i = 1; i < valores.Count; i++)
                ctx.LineTo(new Point(i * passo, Y(valores[i], maximo, altura)), isStroked: true, isSmoothJoin: false);
        }

        geometria.Freeze();
        dc.DrawGeometry(null, linha, geometria);

        if (!string.IsNullOrEmpty(Rotulo))
            DesenharRotulo(dc, Rotulo, largura, altura);
    }

    private static double Y(double valor, double maximo, double altura)
    {
        var proporcao = Math.Clamp(valor / maximo, 0, 1);
        return altura - proporcao * altura;
    }

    private void DesenharRotulo(DrawingContext dc, string texto, double largura, double altura)
    {
        var formatado = new FormattedText(
            texto,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatado, new Point(largura - formatado.Width - 2, altura - formatado.Height - 1));
    }
}
