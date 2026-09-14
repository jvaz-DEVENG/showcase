using GameBoost.Core.Modules.Tweaks;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Página de tweaks e serviços (seções 5.8 e 5.7).
///
/// A confirmação desta tela é a mais séria do app depois da do desinstalador:
/// aqui o usuário está alterando o Windows, não apagando arquivo. Por isso ela
/// separa o que precisa de reinício, o que é de risco médio ou alto, e diz
/// exatamente onde o estado anterior ficou guardado.
/// </summary>
public sealed class TweaksPageViewModel : ModulePageViewModel
{
    public TweaksPageViewModel(TweaksModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
    }

    public override string Nome => "Tweaks";
    public override string Titulo => "Ajustes do Windows";
    public override string Subtitulo => "Cada ajuste com o efeito real, não com a promessa. Nada vem marcado.";
    public override string Icone => "\uE90F";

    /// <summary>
    /// Ler o estado de 24 ajustes custa milissegundos: registro e consulta de
    /// servico. Vale revarrer sozinho para o badge de cada linha mostrar como
    /// as coisas ficaram, e nao como estavam.
    /// </summary>
    protected override bool RevarrerAposAgir => true;

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var nomes = string.Join("\n  ", selecionados.Select(i => i.Titulo));

        var reboot = selecionados
            .Select(i => i.Item.Payload)
            .OfType<TweaksModule.TweakEstado>()
            .Where(e => e.Definicao.ExigeReboot)
            .Select(e => e.Definicao.Nome)
            .ToList();

        var servicos = selecionados.Count(i => i.Item.Id.StartsWith("servico:", StringComparison.Ordinal));

        var texto = $"Aplicar {selecionados.Count} ajuste(s)?\n\n  {nomes}\n\n";

        if (servicos > 0)
        {
            texto += $"{servicos} deles mudam o tipo de inicialização de um serviço do Windows. "
                   + "O estado atual de cada um fica guardado e volta pelo botão Reverter.\n\n";
        }

        if (reboot.Count > 0)
        {
            texto += $"{reboot.Count} só passa(m) a valer depois de reiniciar o computador:\n  "
                   + string.Join("\n  ", reboot)
                   + "\n\nAté lá, nada muda na prática.\n\n";
        }

        var arriscados = selecionados
            .Where(i => i.Item.Risco != Core.Modules.RiskLevel.Baixo)
            .ToList();

        if (arriscados.Count > 0)
        {
            texto += "Ajustes de risco médio ou alto nesta seleção:\n  "
                   + string.Join("\n  ", arriscados.Select(i => $"{i.Titulo} — {i.Item.ComoDesfazer}"))
                   + "\n\n";
        }

        texto += "Tudo isto é reversível: o valor anterior de cada chave vai para o histórico "
               + "antes de qualquer escrita, e Configurações → Histórico desfaz um por um.\n\n"
               + "Continuar?";

        return texto;
    }
}
