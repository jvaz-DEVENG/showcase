using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.Uninstaller;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Página de aplicativos (seção 5.3). Herda a tela inteira do padrão de módulo;
/// só precisa dizer o que perguntar antes de desinstalar.
/// </summary>
public sealed class AppsPageViewModel : ModulePageViewModel
{
    public AppsPageViewModel(UninstallerModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
    }

    public override string Nome => "Apps";
    public override string Titulo => "Aplicativos instalados";
    public override string Subtitulo => "Desinstala em lote, inclusive apps da Store, e mostra o que ficou para trás.";
    public override string Icone => "\uE71D";

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var total = selecionados.Sum(i => i.Item.GanhoBytes);
        var nomes = string.Join("\n  ", selecionados.Take(10).Select(i => i.Titulo));
        var extra = selecionados.Count > 10 ? $"\n  ... e mais {selecionados.Count - 10}" : string.Empty;

        var apps = selecionados.Select(i => i.Item.Payload as InstalledApp).Where(a => a is not null).ToList();
        var semSilencio = apps.Count(a => !a!.TemDesinstalacaoSilenciosa);

        var texto = $"Desinstalar {selecionados.Count} aplicativo(s), liberando cerca de "
                  + $"{GameModeModule.Formatar(total)}?\n\n  {nomes}{extra}\n\n";

        if (semSilencio > 0)
        {
            texto += $"{semSilencio} deles não têm desinstalação silenciosa: o desinstalador "
                   + "próprio de cada um vai abrir, e você conclui por ele.\n\n";
        }

        texto += "Isto não tem como ser desfeito pelo GameBoost. Configurações e dados dos "
               + "aplicativos se perdem.\n\nConsidere criar um ponto de restauração antes "
               + "(está em Ferramentas). Continuar?";

        return texto;
    }
}
