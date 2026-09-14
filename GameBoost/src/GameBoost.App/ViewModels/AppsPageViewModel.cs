using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.Uninstaller;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>Uma pasta de resto, na aba própria.</summary>
public sealed partial class RestoViewModel : ObservableObject
{
    public RestoViewModel(Resto resto)
    {
        Item = resto;
    }

    public Resto Item { get; }

    /// <summary>Nunca pré-marcado: o casamento de nomes é heurística.</summary>
    [ObservableProperty] private bool _selecionado;

    public string Titulo => Item.Nome;
    public string Descricao => $"{Item.Local} · {GameModeModule.Formatar(Item.Bytes)} · {Item.Idade}";
    public string Caminho => Item.Caminho;
}

/// <summary>
/// Página de aplicativos (seção 5.3), em três abas.
///
/// - **Instalados**: a lista principal, herdada do padrão de módulo.
/// - **Atualizações**: o que o winget tem de versão nova.
/// - **Restos de apps antigos**: pastas sem app correspondente.
///
/// As três compartilham o mesmo rodapé conceitual, mas cada uma age sobre a sua
/// lista: aplicar na aba de atualizações não desinstala nada, e vice-versa.
/// </summary>
public sealed partial class AppsPageViewModel : ModulePageViewModel
{
    private readonly UninstallerModule _modulo;

    public AppsPageViewModel(UninstallerModule modulo, IStateBackup backup)
        : base(modulo, backup)
    {
        _modulo = modulo;
    }

    public override string Nome => "Apps";
    public override string Titulo => "Aplicativos instalados";
    public override string Subtitulo => "Desinstala em lote, inclusive apps da Store, e mostra o que ficou para trás.";
    public override string Icone => "\uE71D";

    // ==================================================================
    // Aba: Atualizações
    // ==================================================================

    public ObservableCollection<ActionItemViewModel> Atualizacoes { get; } = new();

    [ObservableProperty] private string _resumoDeAtualizacoes = "Clique em Verificar para consultar o winget.";
    [ObservableProperty] private string _statusDeAtualizacoes = string.Empty;
    [ObservableProperty] private bool _wingetAusente;
    [ObservableProperty] private bool _ocupadoComAtualizacoes;

    public int AtualizacoesSelecionadas => Atualizacoes.Count(a => a.Selecionado);

    public string TextoAtualizar => AtualizacoesSelecionadas == 1
        ? "Atualizar 1 selecionado"
        : $"Atualizar {AtualizacoesSelecionadas} selecionados";

    [RelayCommand]
    public async Task VerificarAtualizacoesAsync()
    {
        if (OcupadoComAtualizacoes)
            return;

        OcupadoComAtualizacoes = true;
        Atualizacoes.Clear();
        ResumoDeAtualizacoes = "Consultando o winget...";

        try
        {
            var progresso = new Progress<ModuleProgress>(p =>
            {
                StatusDeAtualizacoes = p.Etapa;
                Progresso = p.Percentual;
            });

            var resultado = await _modulo.VarrerAtualizacoesAsync(progresso, CancellationToken.None);

            WingetAusente = !_modulo.Winget.Disponivel;

            foreach (var item in resultado.Itens)
            {
                var vm = new ActionItemViewModel(item);
                vm.PropertyChanged += AoMudarSelecaoDeAtualizacao;
                Atualizacoes.Add(vm);
            }

            ResumoDeAtualizacoes = resultado.Resumo;
            StatusDeAtualizacoes = resultado.Avisos.Count > 0
                ? string.Join("  ", resultado.Avisos)
                : "Nada vem marcado. Marque o que quiser atualizar.";
        }
        catch (OperationCanceledException)
        {
            StatusDeAtualizacoes = "Consulta cancelada.";
        }
        finally
        {
            OcupadoComAtualizacoes = false;
            Progresso = 100;
            NotificarAtualizacoes();
        }
    }

    [RelayCommand]
    public async Task AtualizarSelecionadosAsync()
    {
        if (OcupadoComAtualizacoes || !EhAdministrador)
            return;

        var selecionados = Atualizacoes.Where(a => a.Selecionado && a.PodeSelecionar).ToList();

        if (selecionados.Count == 0)
            return;

        if (!DryRun && !ConfirmarAtualizacao(selecionados))
            return;

        OcupadoComAtualizacoes = true;

        try
        {
            var progresso = new Progress<ModuleProgress>(p =>
            {
                StatusDeAtualizacoes = p.Etapa;
                Progresso = p.Percentual;
            });

            var resultado = await _modulo.AtualizarAsync(
                selecionados.Select(s => s.Item.Id).ToList(), DryRun, progresso, CancellationToken.None);

            ResumoDeAtualizacoes = resultado.Resumo;

            var falhas = resultado.Acoes.Where(a => !a.Sucesso).ToList();

            StatusDeAtualizacoes = falhas.Count == 0
                ? "Tudo certo. Verifique de novo para confirmar."
                : string.Join("  ", falhas.Select(f => f.Detalhe));

            if (!DryRun)
                await VerificarAtualizacoesAsync();
        }
        catch (OperationCanceledException)
        {
            StatusDeAtualizacoes = "Atualização cancelada.";
        }
        finally
        {
            OcupadoComAtualizacoes = false;
        }
    }

    /// <summary>Abre a página do App Installer na Microsoft Store.</summary>
    [RelayCommand]
    private void InstalarWinget()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WingetService.LinkDaStore) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusDeAtualizacoes = $"Não foi possível abrir a Store: {ex.Message}";
        }
    }

    private bool ConfirmarAtualizacao(IReadOnlyList<ActionItemViewModel> selecionados)
    {
        var nomes = string.Join("\n  ", selecionados.Take(12).Select(s => s.Titulo));
        var extra = selecionados.Count > 12 ? $"\n  ... e mais {selecionados.Count - 12}" : string.Empty;

        var arriscados = selecionados.Count(s => s.Item.Risco != RiskLevel.Baixo);

        var texto = $"Atualizar {selecionados.Count} aplicativo(s) pelo winget?\n\n  {nomes}{extra}\n\n";

        if (arriscados > 0)
        {
            texto += $"{arriscados} deles são runtime, se atualizam sozinhos ou têm versão "
                   + "instalada desconhecida. O motivo aparece em cada linha.\n\n";
        }

        texto += "Cada app é atualizado por vez, pelo instalador do próprio fabricante. "
               + "Não há como voltar a versão pelo winget: se a versão nova der problema, "
               + "a reinstalação da antiga é pelo site do fabricante.\n\n"
               + "Feche os aplicativos que estiverem abertos antes de continuar.\n\nContinuar?";

        return System.Windows.MessageBox.Show(
            texto, "Atualizar aplicativos",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.OK;
    }

    private void AoMudarSelecaoDeAtualizacao(object? remetente, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionItemViewModel.Selecionado))
            NotificarAtualizacoes();
    }

    private void NotificarAtualizacoes()
    {
        OnPropertyChanged(nameof(AtualizacoesSelecionadas));
        OnPropertyChanged(nameof(TextoAtualizar));
    }

    // ==================================================================
    // Aba: Restos de apps antigos
    // ==================================================================

    public ObservableCollection<RestoViewModel> Restos { get; } = new();

    [ObservableProperty] private string _resumoDeRestos =
        "Clique em Varrer restos para procurar pastas de programas que você já desinstalou.";

    [ObservableProperty] private string _statusDeRestos = string.Empty;
    [ObservableProperty] private bool _ocupadoComRestos;

    public int RestosSelecionados => Restos.Count(r => r.Selecionado);

    public string TextoRemoverRestos => RestosSelecionados == 1
        ? "Mandar 1 para a Lixeira"
        : $"Mandar {RestosSelecionados} para a Lixeira";

    [RelayCommand]
    public async Task VarrerRestosAsync()
    {
        if (OcupadoComRestos)
            return;

        OcupadoComRestos = true;
        Restos.Clear();
        ResumoDeRestos = "Procurando...";

        try
        {
            // A varredura de restos precisa da lista de instalados para saber o
            // que é resto. Sem ela, tudo viraria resto.
            if (_modulo.UltimaLista.Count == 0)
            {
                StatusDeRestos = "Varrendo os apps instalados primeiro...";
                await _modulo.ScanAsync(null, CancellationToken.None);
            }

            var progresso = new Progress<string>(texto => StatusDeRestos = texto);

            var achados = await Task.Run(
                () => _modulo.VarrerRestos(progresso, CancellationToken.None));

            foreach (var resto in achados)
            {
                var vm = new RestoViewModel(resto);
                vm.PropertyChanged += AoMudarSelecaoDeResto;
                Restos.Add(vm);
            }

            var total = achados.Sum(r => r.Bytes);

            ResumoDeRestos = achados.Count == 0
                ? "Nenhuma pasta sem app correspondente."
                : $"{achados.Count} pastas sem app correspondente, {GameModeModule.Formatar(total)} no total.";

            StatusDeRestos = "O casamento de nomes é uma heurística: uma pasta pode pertencer "
                           + "a um app que o registro não declara. Por isso nada vem marcado e "
                           + "a remoção vai para a Lixeira.";
        }
        catch (OperationCanceledException)
        {
            StatusDeRestos = "Varredura cancelada.";
        }
        finally
        {
            OcupadoComRestos = false;
            NotificarRestos();
        }
    }

    [RelayCommand]
    public void RemoverRestos()
    {
        var escolhidos = Restos.Where(r => r.Selecionado).ToList();

        if (escolhidos.Count == 0 || !EhAdministrador)
            return;

        var nomes = string.Join("\n  ", escolhidos.Take(12).Select(r => $"{r.Titulo} — {r.Caminho}"));
        var extra = escolhidos.Count > 12 ? $"\n  ... e mais {escolhidos.Count - 12}" : string.Empty;
        var total = escolhidos.Sum(r => r.Item.Bytes);

        var texto = $"Mandar {escolhidos.Count} pasta(s) para a Lixeira, "
                  + $"liberando {GameModeModule.Formatar(total)}?\n\n  {nomes}{extra}\n\n"
                  + "Elas vão para a Lixeira, não são apagadas de vez: se alguma pertencer a um "
                  + "app que você ainda usa, dá para restaurar.\n\nContinuar?";

        if (System.Windows.MessageBox.Show(
                texto, "Remover restos",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        var (removidos, bytes) = _modulo.RemoverRestos(escolhidos.Select(r => r.Item).ToList());

        foreach (var removido in escolhidos.Take(removidos))
            Restos.Remove(removido);

        ResumoDeRestos = $"{removidos} pasta(s) na Lixeira, {GameModeModule.Formatar(bytes)} liberados.";
        StatusDeRestos = "Para recuperar espaço de verdade, esvazie a Lixeira — está em Ferramentas.";
        NotificarRestos();
    }

    private void AoMudarSelecaoDeResto(object? remetente, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RestoViewModel.Selecionado))
            NotificarRestos();
    }

    private void NotificarRestos()
    {
        OnPropertyChanged(nameof(RestosSelecionados));
        OnPropertyChanged(nameof(TextoRemoverRestos));
    }

    // ==================================================================
    // Aba: Instalados
    // ==================================================================

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
