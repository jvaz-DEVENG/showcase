using GameBoost.Core.Modules.Cleaner;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Página de Limpeza (seção 5.2).
///
/// Toda a tela vem de ModulePageViewModel: cabeçalho com Varrer, lista genérica
/// de ActionItem, rodapé com "Aplicar N selecionados". Este arquivo só precisa
/// dizer como a página se chama e o que perguntar antes de apagar — que era
/// exatamente a promessa do padrão criado junto com o shell.
/// </summary>
public sealed class LimpezaPageViewModel : ModulePageViewModel
{
    public LimpezaPageViewModel(CleanerModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
    }

    public override string Nome => "Limpeza";
    public override string Titulo => "Limpeza de temporários e caches";
    public override string Subtitulo => "Libera espaço mostrando exatamente o que sai e quanto ocupa.";
    public override string Icone => "\uE74D";

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var total = selecionados.Sum(i => i.Item.GanhoBytes);
        var nomes = string.Join("\n  ", selecionados.Take(10).Select(i => i.Titulo));
        var extra = selecionados.Count > 10 ? $"\n  ... e mais {selecionados.Count - 10}" : string.Empty;

        var texto = $"O GameBoost vai limpar {selecionados.Count} item(ns), "
                  + $"liberando cerca de {Core.Modules.GameMode.GameModeModule.Formatar(total)}:\n\n"
                  + $"  {nomes}{extra}\n\n";

        // Avisa de novo, na hora de confirmar, o que tem efeito colateral.
        var comAviso = selecionados
            .Select(i => i.Item.Payload as AchadoDeLimpeza)
            .Where(a => a?.Alvo.Advertencia is not null)
            .Select(a => $"  - {a!.Alvo.Titulo}: {a.Alvo.Advertencia}")
            .ToList();

        if (comAviso.Count > 0)
            texto += "Atenção:\n" + string.Join("\n", comAviso) + "\n\n";

        texto += "Arquivos em uso são mantidos. Continuar?";
        return texto;
    }
}
