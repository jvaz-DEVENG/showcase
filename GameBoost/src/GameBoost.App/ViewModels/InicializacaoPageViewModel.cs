using GameBoost.Core.Modules.Startup;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Página de inicialização (seção 5.6). Também herda a tela do padrão de módulo:
/// "Aplicar N selecionados" aqui significa desativar, e "Reverter" reativa.
/// </summary>
public sealed class InicializacaoPageViewModel : ModulePageViewModel
{
    public InicializacaoPageViewModel(StartupModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
    }

    public override string Nome => "Inicializacao";
    public override string Titulo => "O que abre com o Windows";
    public override string Subtitulo => "Podar a inicialização é, junto com o plano de energia, o ajuste de maior valor.";
    public override string Icone => "\uE7E8";

    protected override string MontarConfirmacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var nomes = string.Join("\n  ", selecionados.Take(12).Select(i => i.Titulo));
        var extra = selecionados.Count > 12 ? $"\n  ... e mais {selecionados.Count - 12}" : string.Empty;

        return $"Impedir que {selecionados.Count} programa(s) abram junto com o Windows?\n\n"
             + $"  {nomes}{extra}\n\n"
             + "Os programas continuam instalados e você pode abrir quando quiser. "
             + "O efeito aparece no próximo boot.\n\n"
             + "Para reativar, use o botão Reverter aqui, o Gerenciador de Tarefas ou "
             + "Configurações, Aplicativos, Inicializar.";
    }
}
