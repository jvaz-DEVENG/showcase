using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;
using GameBoost.Core.State;

namespace GameBoost.App.ViewModels;

/// <summary>Uma linha do Historico: uma alteracao de sistema ja aplicada.</summary>
public sealed partial class HistoricoItemViewModel : ObservableObject
{
    public HistoricoItemViewModel(ChangeRecord record)
    {
        Record = record;
        _revertido = record.Revertido;
    }

    public ChangeRecord Record { get; }

    [ObservableProperty] private bool _revertido;

    public string Quando => Record.Data.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Modulo => Record.Modulo;
    public string Tipo => Record.Tipo.ToString();

    public string Descricao => Record.Extras.TryGetValue("nome", out var nome) && !string.IsNullOrWhiteSpace(nome)
        ? nome
        : string.IsNullOrEmpty(Record.SubAlvo) ? Record.Alvo : $"{Record.Alvo}\\{Record.SubAlvo}";

    public string Mudanca => Record.ValorAnteriorExistia
        ? $"{Record.ValorAnterior ?? "-"}  ->  {Record.ValorNovo ?? "-"}"
        : $"(nao existia)  ->  {Record.ValorNovo ?? "-"}";

    public string Situacao => Revertido ? "Revertido" : "Aplicado";
    public bool PodeDesfazer => !Revertido;
}

/// <summary>
/// Configuracoes e Historico (secao 6). O Historico le direto o state-backup:
/// e a mesma fonte que o "Reverter tudo" usa, entao nunca fica dessincronizado.
/// </summary>
public sealed partial class ConfiguracoesPageViewModel : PageViewModelBase
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly IStateBackup _backup;
    private readonly IRollbackEngine _rollback;
    private readonly AppPaths _paths;
    private readonly IGameBoostLogger _log;
    private bool _carregando;

    public ConfiguracoesPageViewModel(
        AppSettings settings,
        ISettingsStore store,
        IStateBackup backup,
        IRollbackEngine rollback,
        AppPaths paths,
        IGameBoostLogger log)
    {
        _settings = settings;
        _store = store;
        _backup = backup;
        _rollback = rollback;
        _paths = paths;
        _log = log;

        _carregando = true;
        _temaEscuro = !string.Equals(settings.Tema, "Light", StringComparison.OrdinalIgnoreCase);
        _iniciarComWindows = settings.IniciarComWindows;
        _permitirAcessoARede = settings.PermitirAcessoARede;
        _iniciarMinimizado = settings.IniciarMinimizado;
        _carregando = false;

        EhAdministrador = CoreServices.RodandoComoAdministrador();
        RecarregarHistorico();
    }

    public override string Nome => "Configuracoes";
    public override string Titulo => "Configuracoes";
    public override string Subtitulo => "Preferencias do app e historico de tudo que ele ja alterou.";
    public override string Icone => "\uE713";

    public ObservableCollection<HistoricoItemViewModel> Historico { get; } = new();

    [ObservableProperty] private bool _temaEscuro;
    [ObservableProperty] private bool _iniciarComWindows;
    [ObservableProperty] private bool _iniciarMinimizado;
    [ObservableProperty] private bool _permitirAcessoARede;
    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private string _status = string.Empty;

    public string PastaDeDados => _paths.RootDirectory;

    public string ModoDeArmazenamento => _paths.Portatil
        ? "Modo portatil: os dados ficam ao lado do executavel."
        : "Os dados ficam em %LOCALAPPDATA%\\GameBoost.";

    public bool HistoricoVazio => Historico.Count == 0;

    public override void AoEntrar() => RecarregarHistorico();

    [RelayCommand]
    private void RecarregarHistorico()
    {
        _backup.Recarregar();
        Historico.Clear();

        // Mais recente primeiro: e o que o usuario quer desfazer.
        foreach (var record in _backup.Todos.OrderByDescending(r => r.Data))
            Historico.Add(new HistoricoItemViewModel(record));

        OnPropertyChanged(nameof(HistoricoVazio));
    }

    [RelayCommand]
    private void Desfazer(HistoricoItemViewModel? item)
    {
        if (item is null || !EhAdministrador)
            return;

        var resposta = MessageBox.Show(
            $"Desfazer esta alteracao?\n\n{item.Descricao}\n{item.Mudanca}",
            "Desfazer", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes)
            return;

        var resultado = _rollback.Reverter(new[] { item.Record.Id }, dryRun: false).FirstOrDefault();

        if (resultado is null)
        {
            Status = "Esta alteracao ja havia sido revertida.";
            RecarregarHistorico();
            return;
        }

        item.Revertido = resultado.Sucesso;
        Status = resultado.Detalhe;
        _log.Info("Config", "Desfazer", item.Record.Alvo, resultado.Detalhe);

        RecarregarHistorico();
    }

    [RelayCommand]
    private void AbrirPastaDeDados()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _paths.RootDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = "Nao foi possivel abrir a pasta.";
        }
    }

    private void Salvar()
    {
        if (_carregando)
            return;

        _settings.Tema = TemaEscuro ? "Dark" : "Light";
        _settings.IniciarComWindows = IniciarComWindows;
        _settings.PermitirAcessoARede = PermitirAcessoARede;
        _settings.IniciarMinimizado = IniciarMinimizado;
        _store.Save(_settings);
    }

    partial void OnTemaEscuroChanged(bool value) => Salvar();

    partial void OnIniciarComWindowsChanged(bool value)
    {
        Salvar();

        // Honestidade (secao 6): o GameBoost aparece na propria lista de
        // inicializacao que ele mesmo gerencia.
        Status = value
            ? "O GameBoost passa a iniciar com o Windows. Ele aparece na propria lista de Inicializacao."
            : "O GameBoost nao inicia mais com o Windows.";
    }

    partial void OnIniciarMinimizadoChanged(bool value) => Salvar();

    partial void OnPermitirAcessoARedeChanged(bool value)
    {
        Salvar();

        Status = value
            ? "O teste de velocidade da página Rede foi liberado. Nenhum dado seu é enviado: "
            + "o teste só baixa e envia bytes aleatórios para medir a conexão."
            : "Acesso à internet desligado. O teste de velocidade fica indisponível; "
            + "ping, DNS e detecção de NAT continuam funcionando.";
    }
}
