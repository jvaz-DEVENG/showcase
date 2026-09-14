using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameBoost.App.ViewModels;

/// <summary>Uma das três telas do onboarding.</summary>
public sealed record TelaDeOnboarding(string Titulo, string Texto, string Rodape);

/// <summary>
/// Onboarding da primeira abertura (seção 6).
///
/// Três telas, e o conteúdo delas não é propaganda: é o contrato. O usuário
/// precisa saber, antes de clicar em qualquer coisa, que tudo volta atrás, que
/// nada é enviado para lugar nenhum, e que a maior parte do que os boosters
/// prometem não existe. Um onboarding que vendesse FPS seria a primeira mentira
/// do aplicativo.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public static IReadOnlyList<TelaDeOnboarding> Telas { get; } = new[]
    {
        new TelaDeOnboarding(
            "Tudo o que ele faz, ele desfaz",
            "Antes de alterar qualquer coisa no Windows, o GameBoost grava o valor anterior. "
          + "Cada ação tem um botão de desfazer, e Configurações → Histórico mostra tudo o que "
          + "já foi feito, com a data.\n\n"
          + "Se o aplicativo travar ou o computador reiniciar no meio de uma sessão, a próxima "
          + "abertura avisa e oferece a restauração.\n\n"
          + "Nada é aplicado sem você confirmar, e nada vem marcado — nem o que o próprio "
          + "GameBoost recomenda.",
            "Reversível por construção, não por promessa."),

        new TelaDeOnboarding(
            "Nada sai daqui",
            "O GameBoost não tem telemetria. Não há conta, não há login, não há servidor "
          + "recebendo dados sobre você ou sobre esta máquina.\n\n"
          + "Tudo o que ele lê fica em %LOCALAPPDATA%\\GameBoost, em arquivos de texto que você "
          + "pode abrir e ler.\n\n"
          + "As únicas funções que usam a internet são o teste de velocidade e a checagem de "
          + "atualizações de aplicativos. A primeira vem desligada e só liga se você mandar.",
            "Sem conta, sem nuvem, sem telemetria."),

        new TelaDeOnboarding(
            "O que faz diferença de verdade",
            "A maior parte do que os \"otimizadores\" prometem não existe. Limpar registro não "
          + "acelera nada. Desfragmentar SSD só desgasta a unidade. Fechar processo do Windows "
          + "à toa quebra mais do que ajuda.\n\n"
          + "O que rende de verdade, nesta ordem: podar o que abre com o Windows, liberar espaço "
          + "em disco quando ele está cheio, manter o driver de vídeo atual, e usar cabo em vez "
          + "de Wi-Fi para jogo online.\n\n"
          + "Cada ajuste desta ferramenta diz o efeito real que tem. Quando é marginal, está "
          + "escrito que é marginal.",
            "Números medidos, não estimados.")
    };

    [ObservableProperty] private int _indice;

    public TelaDeOnboarding Atual => Telas[Indice];

    public bool EhPrimeira => Indice == 0;
    public bool EhUltima => Indice == Telas.Count - 1;

    public string TextoDoAvancar => EhUltima ? "Começar" : "Próxima";
    public string Passo => $"{Indice + 1} de {Telas.Count}";

    /// <summary>Marcado na última tela: abre o relatório de saúde ao terminar.</summary>
    [ObservableProperty] private bool _abrirRelatorio = true;

    /// <summary>Fechado, com ou sem "começar".</summary>
    public event Action<bool>? AoTerminar;

    [RelayCommand]
    private void Avancar()
    {
        if (EhUltima)
        {
            AoTerminar?.Invoke(AbrirRelatorio);
            return;
        }

        Indice++;
    }

    [RelayCommand]
    private void Voltar()
    {
        if (!EhPrimeira)
            Indice--;
    }

    [RelayCommand]
    private void Pular() => AoTerminar?.Invoke(false);

    partial void OnIndiceChanged(int value)
    {
        OnPropertyChanged(nameof(Atual));
        OnPropertyChanged(nameof(EhPrimeira));
        OnPropertyChanged(nameof(EhUltima));
        OnPropertyChanged(nameof(TextoDoAvancar));
        OnPropertyChanged(nameof(Passo));
    }
}
