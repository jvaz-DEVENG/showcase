using System.Drawing;
using System.Windows.Forms;
using GameBoost.Core.Logging;

namespace GameBoost.App.Services;

/// <summary>
/// Ícone na bandeja com o menu da seção 6: Ativar Modo Game, Limpar RAM, Abrir,
/// Sair.
///
/// Duas decisões que valem ser ditas:
///
/// - **Fechar a janela não fecha o app quando a bandeja está ligada**, mas o
///   menu tem "Sair" e ele sai de verdade. Programa que só finge fechar e fica
///   rodando escondido é queixa legítima; o caminho para sair existe e está
///   visível.
/// - **O ícone é desenhado em código**, não carregado de arquivo. Num publish
///   single-file, um `.ico` solto ao lado do exe não existe, e embutir recurso
///   para dezesseis pixels não vale.
/// </summary>
public sealed class BandejaDoSistema : IDisposable
{
    private readonly IGameBoostLogger _log;
    private NotifyIcon? _icone;
    private ToolStripMenuItem? _itemModoGame;

    public BandejaDoSistema(IGameBoostLogger log)
    {
        _log = log;
    }

    public event Action? AoAbrir;
    public event Action? AoAlternarModoGame;
    public event Action? AoLimparRam;
    public event Action? AoSair;

    public bool Visivel => _icone is { Visible: true };

    public void Mostrar()
    {
        if (_icone is not null)
        {
            _icone.Visible = true;
            return;
        }

        try
        {
            var menu = new ContextMenuStrip();

            _itemModoGame = new ToolStripMenuItem("Ativar Modo Game");
            _itemModoGame.Click += (_, _) => AoAlternarModoGame?.Invoke();
            menu.Items.Add(_itemModoGame);

            var limpar = new ToolStripMenuItem("Limpar RAM");
            limpar.Click += (_, _) => AoLimparRam?.Invoke();
            menu.Items.Add(limpar);

            menu.Items.Add(new ToolStripSeparator());

            var abrir = new ToolStripMenuItem("Abrir GameBoost");
            abrir.Click += (_, _) => AoAbrir?.Invoke();
            menu.Items.Add(abrir);

            var sair = new ToolStripMenuItem("Sair");
            sair.Click += (_, _) => AoSair?.Invoke();
            menu.Items.Add(sair);

            _icone = new NotifyIcon
            {
                Icon = Desenhar(false),
                Text = "GameBoost",
                Visible = true,
                ContextMenuStrip = menu
            };

            _icone.DoubleClick += (_, _) => AoAbrir?.Invoke();

            _log.Info("bandeja", "Mostrar", null, "ícone na bandeja");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _log.Error("bandeja", "Mostrar", null, ex.Message, ex);
        }
    }

    public void Esconder()
    {
        if (_icone is not null)
            _icone.Visible = false;
    }

    /// <summary>Muda o texto do menu e a cor do ícone conforme o Modo Game.</summary>
    public void AtualizarModoGame(bool ativo)
    {
        if (_itemModoGame is not null)
            _itemModoGame.Text = ativo ? "Desligar Modo Game" : "Ativar Modo Game";

        if (_icone is null)
            return;

        var antigo = _icone.Icon;
        _icone.Icon = Desenhar(ativo);
        _icone.Text = ativo ? "GameBoost — Modo Game ativo" : "GameBoost";

        // O handle do ícone antigo não sai sozinho: trocar o ícone a cada
        // sessão vazaria um handle por vez.
        antigo?.Dispose();
    }

    /// <summary>Balão da bandeja. Usado para o convite de Modo Game.</summary>
    public void Avisar(string titulo, string texto)
    {
        if (_icone is null)
            return;

        try
        {
            _icone.BalloonTipTitle = titulo;
            _icone.BalloonTipText = texto;
            _icone.BalloonTipIcon = ToolTipIcon.Info;
            _icone.ShowBalloonTip(8000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _log.Warn("bandeja", "Avisar", titulo, ex.Message);
        }
    }

    /// <summary>
    /// Desenha um ícone de 16×16: um quadrado arredondado com a inicial. Verde
    /// quando o Modo Game está ativo, cinza quando não.
    /// </summary>
    private static Icon Desenhar(bool ativo)
    {
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var cor = ativo
            ? Color.FromArgb(0x2E, 0xC4, 0x9A)
            : Color.FromArgb(0x8A, 0x90, 0x99);

        using var pincel = new SolidBrush(cor);
        g.FillEllipse(pincel, 1, 1, 14, 14);

        using var fonte = new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel);
        using var texto = new SolidBrush(Color.FromArgb(0x10, 0x12, 0x16));

        g.DrawString("G", fonte, texto, new PointF(4, 3));

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        if (_icone is null)
            return;

        _icone.Visible = false;
        _icone.Icon?.Dispose();
        _icone.Dispose();
        _icone = null;
    }
}
