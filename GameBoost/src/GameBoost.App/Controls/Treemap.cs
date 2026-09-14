using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameBoost.Core.Modules.DiskAnalyzer;

namespace GameBoost.App.Controls;

/// <summary>Um retângulo desenhado, guardado para achar o nó sob o mouse.</summary>
internal sealed record Bloco(DiskNode No, Rect Area);

/// <summary>
/// Treemap da seção 5.4: retângulos proporcionais ao tamanho, coloridos por
/// categoria.
///
/// Usa o algoritmo "squarified": em vez de fatiar sempre na mesma direção, ele
/// escolhe a cada passo a direção que deixa os retângulos mais próximos de
/// quadrados. Fatias compridas e finas são impossíveis de clicar e não deixam
/// comparar áreas a olho, que é justamente para o que o treemap serve.
///
/// Desenha em OnRender: um disco cheio tem milhares de blocos por nível, e
/// criar objetos visuais para cada um travaria a navegação.
/// </summary>
public sealed class Treemap : Control
{
    private readonly List<Bloco> _blocos = new();
    private DiskNode? _sobMouse;

    public static readonly DependencyProperty RaizProperty = DependencyProperty.Register(
        nameof(Raiz), typeof(DiskNode), typeof(Treemap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximoDeBlocosProperty = DependencyProperty.Register(
        nameof(MaximoDeBlocos), typeof(int), typeof(Treemap),
        new FrameworkPropertyMetadata(120, FrameworkPropertyMetadataOptions.AffectsRender));

    public DiskNode? Raiz
    {
        get => (DiskNode?)GetValue(RaizProperty);
        set => SetValue(RaizProperty, value);
    }

    /// <summary>Acima disso os retângulos ficam menores que o clique.</summary>
    public int MaximoDeBlocos
    {
        get => (int)GetValue(MaximoDeBlocosProperty);
        set => SetValue(MaximoDeBlocosProperty, value);
    }

    /// <summary>Disparado ao clicar num bloco que é pasta: o shell desce um nível.</summary>
    public event Action<DiskNode>? AoEntrarNaPasta;

    public event Action<DiskNode?>? AoPassarOMouse;

    public Treemap()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var no = Encontrar(e.GetPosition(this));
        if (ReferenceEquals(no, _sobMouse))
            return;

        _sobMouse = no;
        AoPassarOMouse?.Invoke(no);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        _sobMouse = null;
        AoPassarOMouse?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var no = Encontrar(e.GetPosition(this));

        // Só pasta desce um nível: clicar num arquivo não leva a lugar nenhum.
        if (no is { EhPasta: true, Filhos.Count: > 0 })
            AoEntrarNaPasta?.Invoke(no);
    }

    private DiskNode? Encontrar(Point ponto)
    {
        for (var i = _blocos.Count - 1; i >= 0; i--)
        {
            if (_blocos[i].Area.Contains(ponto))
                return _blocos[i].No;
        }

        return null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        _blocos.Clear();

        var largura = ActualWidth;
        var altura = ActualHeight;

        if (largura <= 4 || altura <= 4)
            return;

        var fundo = new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x26));
        fundo.Freeze();
        dc.DrawRectangle(fundo, null, new Rect(0, 0, largura, altura));

        var raiz = Raiz;
        if (raiz is null || raiz.Tamanho <= 0)
        {
            Texto(dc, "Varra um disco para ver onde está o espaço", largura / 2, altura / 2, centralizado: true);
            return;
        }

        var filhos = raiz.Filhos
            .Where(f => f.Tamanho > 0)
            .OrderByDescending(f => f.Tamanho)
            .Take(MaximoDeBlocos)
            .ToList();

        if (filhos.Count == 0)
        {
            Texto(dc, "Pasta vazia", largura / 2, altura / 2, centralizado: true);
            return;
        }

        Espalhar(dc, filhos, new Rect(1, 1, largura - 2, altura - 2));
    }

    /// <summary>
    /// Coração do squarified: acumula itens numa faixa enquanto isso melhora a
    /// proporção média dos retângulos, e fecha a faixa quando começa a piorar.
    /// </summary>
    private void Espalhar(DrawingContext dc, List<DiskNode> itens, Rect area)
    {
        var total = itens.Sum(i => (double)i.Tamanho);
        if (total <= 0)
            return;

        var restantes = new List<DiskNode>(itens);

        while (restantes.Count > 0 && area is { Width: > 1, Height: > 1 })
        {
            var somaRestante = restantes.Sum(i => (double)i.Tamanho);
            var horizontal = area.Width >= area.Height;
            var ladoFixo = horizontal ? area.Height : area.Width;

            var faixa = new List<DiskNode>();
            double somaFaixa = 0;
            var melhorProporcao = double.MaxValue;

            foreach (var item in restantes)
            {
                var candidata = somaFaixa + item.Tamanho;
                var proporcao = PiorProporcao(faixa, item, candidata, somaRestante, area, ladoFixo);

                if (faixa.Count > 0 && proporcao > melhorProporcao)
                    break;

                faixa.Add(item);
                somaFaixa = candidata;
                melhorProporcao = proporcao;
            }

            var fracao = somaFaixa / somaRestante;
            var espessura = (horizontal ? area.Width : area.Height) * fracao;

            var posicao = horizontal ? area.Top : area.Left;

            foreach (var item in faixa)
            {
                var parte = somaFaixa <= 0 ? 0 : item.Tamanho / somaFaixa * ladoFixo;

                var retangulo = horizontal
                    ? new Rect(area.Left, posicao, espessura, parte)
                    : new Rect(posicao, area.Top, parte, espessura);

                Desenhar(dc, item, retangulo);
                posicao += parte;
            }

            area = horizontal
                ? new Rect(area.Left + espessura, area.Top, Math.Max(0, area.Width - espessura), area.Height)
                : new Rect(area.Left, area.Top + espessura, area.Width, Math.Max(0, area.Height - espessura));

            foreach (var item in faixa)
                restantes.Remove(item);
        }
    }

    private static double PiorProporcao(
        List<DiskNode> faixa, DiskNode candidato, double somaComCandidato,
        double somaRestante, Rect area, double ladoFixo)
    {
        if (somaComCandidato <= 0 || somaRestante <= 0)
            return double.MaxValue;

        var comprimento = (area.Width >= area.Height ? area.Width : area.Height)
                        * (somaComCandidato / somaRestante);

        if (comprimento <= 0)
            return double.MaxValue;

        var pior = 0.0;

        foreach (var item in faixa.Append(candidato))
        {
            var lado = item.Tamanho / somaComCandidato * ladoFixo;
            if (lado <= 0)
                continue;

            pior = Math.Max(pior, Math.Max(comprimento / lado, lado / comprimento));
        }

        return pior == 0 ? double.MaxValue : pior;
    }

    private void Desenhar(DrawingContext dc, DiskNode no, Rect area)
    {
        if (area.Width < 1 || area.Height < 1)
            return;

        _blocos.Add(new Bloco(no, area));

        var cor = (Color)ColorConverter.ConvertFromString(DiskCategories.Cor(no.Categoria));
        var destacado = ReferenceEquals(no, _sobMouse);

        var preenche = new SolidColorBrush(cor) { Opacity = destacado ? 1.0 : 0.78 };
        preenche.Freeze();

        var borda = new Pen(new SolidColorBrush(Color.FromArgb(destacado ? (byte)255 : (byte)60, 20, 22, 26)),
            destacado ? 2 : 1);
        borda.Freeze();

        // Deflate evita que bordas vizinhas se sobreponham e engrossem.
        var desenho = Rect.Inflate(area, -0.5, -0.5);
        if (desenho.Width <= 0 || desenho.Height <= 0)
            desenho = area;

        dc.DrawRectangle(preenche, borda, desenho);

        // Rótulo só cabe em bloco grande; abaixo disso vira borrão.
        if (area.Width > 64 && area.Height > 26)
        {
            Texto(dc, no.Nome, area.Left + 5, area.Top + 3, centralizado: false, maximo: area.Width - 10);

            if (area.Height > 42)
            {
                Texto(dc, Core.Modules.GameMode.GameModeModule.Formatar(no.Tamanho),
                    area.Left + 5, area.Top + 19, centralizado: false, maximo: area.Width - 10, opaco: true);
            }
        }
    }

    private void Texto(
        DrawingContext dc, string texto, double x, double y,
        bool centralizado, double maximo = double.MaxValue, bool opaco = false)
    {
        var formatado = new FormattedText(
            texto,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11.5,
            new SolidColorBrush(Color.FromArgb(opaco ? (byte)190 : (byte)255, 13, 17, 23)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        if (maximo < double.MaxValue)
            formatado.MaxTextWidth = Math.Max(10, maximo);

        dc.DrawText(formatado, centralizado
            ? new Point(x - formatado.Width / 2, y - formatado.Height / 2)
            : new Point(x, y));
    }
}
