using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.DiskAnalyzer;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Native;

namespace GameBoost.App.ViewModels;

/// <summary>Um item de lista: arquivo grande, pasta grande ou jogo instalado.</summary>
public sealed class ItemDeEspacoViewModel
{
    public ItemDeEspacoViewModel(string nome, string detalhe, long bytes, string caminho, string? marca = null)
    {
        Nome = nome;
        Detalhe = detalhe;
        Bytes = bytes;
        Caminho = caminho;
        Marca = marca;
    }

    public string Nome { get; }
    public string Detalhe { get; }
    public long Bytes { get; }
    public string Caminho { get; }

    /// <summary>Selo curto, como "parado há muito tempo".</summary>
    public string? Marca { get; }

    public string Tamanho => GameModeModule.Formatar(Bytes);
    public bool TemMarca => Marca is not null;
}

/// <summary>
/// Analisador de espaço em disco (seção 5.4) com a biblioteca de jogos (5.11).
///
/// Não implementa IModule: aqui nada é aplicado nem revertido. A página mostra
/// onde está o espaço e abre no Explorer — quem apaga é o usuário, com as
/// ferramentas do Windows.
/// </summary>
public sealed partial class EspacoPageViewModel : PageViewModelBase
{
    private readonly DiskScanner _scanner;
    private readonly GameLibrary _biblioteca;
    private readonly IGameBoostLogger _log;

    private DiskScanResult? _resultado;
    private CancellationTokenSource? _cts;

    public EspacoPageViewModel(DiskScanner scanner, GameLibrary biblioteca, IGameBoostLogger log)
    {
        _scanner = scanner;
        _biblioteca = biblioteca;
        _log = log;

        foreach (var unidade in DiskScanner.VolumesDisponiveis())
            Volumes.Add(unidade.RootDirectory.FullName);

        VolumeSelecionado = Volumes.FirstOrDefault()
            ?? Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?? @"C:\";
    }

    public override string Nome => "Espaco";
    public override string Titulo => "Onde está o seu espaço";
    public override string Subtitulo => "Varre o disco e mostra o que ocupa, do maior para o menor.";
    public override string Icone => "\uE8B7";

    public ObservableCollection<string> Volumes { get; } = new();
    public ObservableCollection<ItemDeEspacoViewModel> MaioresArquivos { get; } = new();
    public ObservableCollection<ItemDeEspacoViewModel> MaioresPastas { get; } = new();
    public ObservableCollection<ItemDeEspacoViewModel> Jogos { get; } = new();
    public ObservableCollection<SpecialFile> Especiais { get; } = new();

    /// <summary>Caminho até a pasta aberta no treemap, para o botão de voltar.</summary>
    public ObservableCollection<DiskNode> Trilha { get; } = new();

    [ObservableProperty] private string _volumeSelecionado = @"C:\";
    [ObservableProperty] private DiskNode? _noAtual;
    [ObservableProperty] private bool _varrendo;
    [ObservableProperty] private bool _jaVarreu;
    [ObservableProperty] private int _progresso;
    [ObservableProperty] private string _status = "Escolha um disco e clique em Varrer.";
    [ObservableProperty] private string _resumo = string.Empty;
    [ObservableProperty] private string? _sobOMouse;

    public bool PodeSubir => Trilha.Count > 1;

    [RelayCommand]
    private async Task VarrerAsync()
    {
        if (Varrendo)
            return;

        _cts = new CancellationTokenSource();
        Varrendo = true;
        Progresso = 0;
        Status = $"Lendo {VolumeSelecionado}...";

        Limpar();

        var volume = VolumeSelecionado;

        try
        {
            var progresso = new Progress<ScanProgresso>(p =>
            {
                Status = $"{p.Arquivos:N0} arquivos, {GameModeModule.Formatar(p.Bytes)}...";

                // Não há total conhecido antes do fim: a barra anda até 95% e
                // completa na conclusão, em vez de fingir uma porcentagem exata.
                Progresso = Math.Min(95, Progresso + 1);
            });

            var resultado = await Task.Run(() => _scanner.Varrer(volume, progresso, _cts.Token), _cts.Token);
            var jogos = await Task.Run(() => _biblioteca.Listar(), _cts.Token);

            _resultado = resultado;
            Preencher(resultado, jogos);

            Progresso = 100;
            JaVarreu = true;
        }
        catch (OperationCanceledException)
        {
            Status = "Varredura cancelada.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Não foi possível varrer {volume}: {ex.Message}";
            _log.Error("Espaco", "Varrer", volume, ex.Message, ex);
        }
        finally
        {
            Varrendo = false;
        }
    }

    private void Limpar()
    {
        MaioresArquivos.Clear();
        MaioresPastas.Clear();
        Jogos.Clear();
        Especiais.Clear();
        Trilha.Clear();
        NoAtual = null;
    }

    private void Preencher(DiskScanResult resultado, IReadOnlyList<JogoInstalado> jogos)
    {
        NoAtual = resultado.Raiz;
        Trilha.Add(resultado.Raiz);

        foreach (var arquivo in resultado.Raiz.MaioresArquivos(100))
        {
            MaioresArquivos.Add(new ItemDeEspacoViewModel(
                arquivo.Nome,
                Path.GetDirectoryName(arquivo.Caminho) ?? arquivo.Caminho,
                arquivo.Tamanho,
                arquivo.Caminho,
                DiskCategories.Nome(arquivo.Categoria)));
        }

        foreach (var pasta in resultado.Raiz.MaioresPastas(50))
        {
            MaioresPastas.Add(new ItemDeEspacoViewModel(
                pasta.Nome,
                $"{pasta.TotalDeArquivos:N0} arquivos · {pasta.Caminho}",
                pasta.Tamanho,
                pasta.Caminho));
        }

        foreach (var jogo in jogos)
        {
            Jogos.Add(new ItemDeEspacoViewModel(
                jogo.Nome,
                $"{jogo.Launcher} · jogado {jogo.QuandoJogou}",
                jogo.Bytes,
                jogo.Pasta,
                jogo.CandidatoADesinstalar ? "parado e grande" : null));
        }

        foreach (var especial in SpecialFiles.Encontrar(resultado.Raiz.Caminho))
            Especiais.Add(especial);

        var ocupado = resultado.EspacoTotal - resultado.EspacoLivre;
        var naoAlcancado = ocupado - resultado.Raiz.Tamanho;

        Resumo = $"{GameModeModule.Formatar(resultado.Raiz.Tamanho)} mapeados em "
               + $"{resultado.TotalDeArquivos:N0} arquivos, em {resultado.Duracao.TotalSeconds:0.0} segundos. "
               + $"Livre: {GameModeModule.Formatar(resultado.EspacoLivre)} de "
               + $"{GameModeModule.Formatar(resultado.EspacoTotal)}.";

        // Honestidade sobre o que a varredura não alcançou (regra 4).
        Status = resultado.PastasIgnoradas > 0
            ? $"{resultado.PastasIgnoradas} pastas não puderam ser lidas (permissão do Windows). "
            + $"Faltam {GameModeModule.Formatar(Math.Max(0, naoAlcancado))} para fechar com o espaço ocupado."
            : "Varredura concluída.";
    }

    // ---------------- Navegação no treemap ----------------

    public void Entrar(DiskNode pasta)
    {
        if (!pasta.EhPasta || pasta.Filhos.Count == 0)
            return;

        Trilha.Add(pasta);
        NoAtual = pasta;
        OnPropertyChanged(nameof(PodeSubir));
    }

    [RelayCommand]
    private void Subir()
    {
        if (Trilha.Count <= 1)
            return;

        Trilha.RemoveAt(Trilha.Count - 1);
        NoAtual = Trilha[^1];
        OnPropertyChanged(nameof(PodeSubir));
    }

    [RelayCommand]
    private void VoltarParaRaiz()
    {
        if (_resultado is null)
            return;

        Trilha.Clear();
        Trilha.Add(_resultado.Raiz);
        NoAtual = _resultado.Raiz;
        OnPropertyChanged(nameof(PodeSubir));
    }

    public void MostrarNoRodape(DiskNode? no)
        => SobOMouse = no is null
            ? null
            : $"{no.Caminho} — {GameModeModule.Formatar(no.Tamanho)}"
            + (no.EhPasta ? $" em {no.TotalDeArquivos:N0} arquivos" : string.Empty);

    // ---------------- Ações ----------------

    /// <summary>
    /// Abre no Explorer com o item já selecionado. O GameBoost não apaga nada
    /// daqui: a seção 5.4 é para mostrar, e quem decide é o usuário.
    /// </summary>
    [RelayCommand]
    private void AbrirNoExplorer(ItemDeEspacoViewModel? item)
    {
        if (item is null)
            return;

        try
        {
            var existeArquivo = File.Exists(item.Caminho);
            var argumento = existeArquivo ? $"/select,\"{item.Caminho}\"" : $"\"{item.Caminho}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = argumento,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = "Não foi possível abrir o Explorer.";
            _log.Warn("Espaco", "AbrirNoExplorer", item.Caminho, ex.Message);
        }
    }

    /// <summary>Manda um item para a Lixeira, com confirmação e sem apagar de vez.</summary>
    [RelayCommand]
    private void MandarParaLixeira(ItemDeEspacoViewModel? item)
    {
        if (item is null)
            return;

        var resposta = MessageBox.Show(
            $"Enviar para a Lixeira?\n\n{item.Caminho}\n{item.Tamanho}\n\n"
            + "Dá para recuperar pela Lixeira do Windows enquanto ela não for esvaziada.",
            "Enviar para a Lixeira", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.Yes)
            return;

        if (RecycleBinBridge.ParaLixeira(new[] { item.Caminho }))
        {
            Status = $"{item.Nome} foi para a Lixeira. Varra de novo para atualizar os números.";
            _log.Info("Espaco", "ParaLixeira", item.Caminho, item.Tamanho);
        }
        else
        {
            Status = "O Windows recusou mover para a Lixeira. O arquivo pode estar em uso.";
        }
    }

    [RelayCommand]
    private void ExplicarEspecial(SpecialFile? arquivo)
    {
        if (arquivo is null)
            return;

        MessageBox.Show(SpecialFiles.Descrever(arquivo), arquivo.Nome,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Cancelar() => _cts?.Cancel();
}
