using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBoost.Core;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Tools;

namespace GameBoost.App.ViewModels;

public sealed partial class FerramentaViewModel : ObservableObject
{
    public FerramentaViewModel(QuickTool ferramenta)
    {
        Ferramenta = ferramenta;
    }

    public QuickTool Ferramenta { get; }

    public string Id => Ferramenta.Id;
    public string Titulo => Ferramenta.Titulo;
    public string Descricao => Ferramenta.Descricao;
    public string Categoria => Ferramenta.Categoria;
    public RiskLevel Risco => Ferramenta.Risco;
    public string? Duracao => Ferramenta.Duracao;
    public bool PrecisaAdmin => Ferramenta.PrecisaAdmin;

    public string TextoDeRisco => Ferramenta.Risco switch
    {
        RiskLevel.Alto => "Risco alto",
        RiskLevel.Medio => "Risco medio",
        _ => "Risco baixo"
    };

    [ObservableProperty] private bool _rodando;
    [ObservableProperty] private string? _resultado;
    [ObservableProperty] private bool _falhou;

    public bool TemResultado => !string.IsNullOrWhiteSpace(Resultado);

    partial void OnResultadoChanged(string? value) => OnPropertyChanged(nameof(TemResultado));
}

/// <summary>
/// Página de Ferramentas rápidas (seção 5.13).
///
/// Não herda de ModulePageViewModel de propósito: aqui não existe lista de
/// itens para marcar e aplicar em lote. São ações independentes, cada uma com
/// a própria confirmação e o próprio resultado.
/// </summary>
public sealed partial class FerramentasPageViewModel : PageViewModelBase
{
    private readonly QuickToolsService _servico;
    private CancellationTokenSource? _cts;

    public FerramentasPageViewModel(QuickToolsService servico)
    {
        _servico = servico;
        EhAdministrador = CoreServices.RodandoComoAdministrador();

        foreach (var ferramenta in QuickToolsCatalog.Todas())
            Ferramentas.Add(new FerramentaViewModel(ferramenta));
    }

    public override string Nome => "Ferramentas";
    public override string Titulo => "Ferramentas rápidas";
    public override string Subtitulo => "Atalhos para o que normalmente exige prompt de comando.";
    public override string Icone => "\uE912";

    public ObservableCollection<FerramentaViewModel> Ferramentas { get; } = new();

    [ObservableProperty] private bool _ehAdministrador;
    [ObservableProperty] private string _status = "Cada ferramenta explica o que faz e como desfazer.";
    [ObservableProperty] private string? _saidaLonga;
    [ObservableProperty] private bool _ocupado;

    public bool TemSaida => !string.IsNullOrWhiteSpace(SaidaLonga);

    partial void OnSaidaLongaChanged(string? value) => OnPropertyChanged(nameof(TemSaida));

    [RelayCommand]
    private async Task ExecutarAsync(FerramentaViewModel? item)
    {
        if (item is null || Ocupado)
            return;

        var f = item.Ferramenta;

        if (f.PrecisaAdmin && !EhAdministrador)
        {
            item.Falhou = true;
            item.Resultado = "Esta ferramenta exige executar o GameBoost como administrador.";
            return;
        }

        if (f.Confirmacao is not null)
        {
            var texto = f.Confirmacao;

            if (f.Duracao is not null)
                texto += $"\n\nIsto {f.Duracao}.";

            var resposta = MessageBox.Show(texto, f.Titulo,
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (resposta != MessageBoxResult.Yes)
                return;
        }

        _cts = new CancellationTokenSource();
        Ocupado = true;
        item.Rodando = true;
        item.Falhou = false;
        item.Resultado = null;
        SaidaLonga = null;
        Status = f.Duracao is null ? $"{f.Titulo}..." : $"{f.Titulo} — {f.Duracao}.";

        // Ferramenta demorada mostra a saída ao vivo: sem isso o usuário acha
        // que travou e mata o app no meio de um sfc.
        var progresso = f.Tipo == ToolKind.Demorada
            ? new Progress<string>(linha => SaidaLonga = (SaidaLonga ?? string.Empty) + linha + Environment.NewLine)
            : null;

        try
        {
            var resultado = await _servico.ExecutarAsync(f.Id, progresso, _cts.Token);

            item.Falhou = !resultado.Sucesso;
            item.Resultado = resultado.Mensagem;

            if (!string.IsNullOrWhiteSpace(resultado.Detalhe))
                SaidaLonga = resultado.Detalhe;

            Status = resultado.Mensagem;
        }
        catch (OperationCanceledException)
        {
            item.Resultado = "Cancelado.";
            Status = "Cancelado.";
        }
        finally
        {
            item.Rodando = false;
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => _cts?.Cancel();

    [RelayCommand]
    private void CopiarSaida()
    {
        if (string.IsNullOrWhiteSpace(SaidaLonga))
            return;

        try
        {
            Clipboard.SetText(SaidaLonga);
            Status = "Copiado. É só colar onde precisar.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // A área de transferência pode estar presa por outro app.
            Status = "Não foi possível copiar: outro programa está usando a área de transferência.";
        }
    }

    [RelayCommand]
    private void LimparSaida() => SaidaLonga = null;

    /// <summary>Explicação do botão "?": o que faz, risco e como desfazer.</summary>
    [RelayCommand]
    private void Explicar(FerramentaViewModel? item)
    {
        if (item is null)
            return;

        var f = item.Ferramenta;
        var texto = $"O que faz: {f.Descricao}\n\nRisco: {item.TextoDeRisco}";

        if (f.Duracao is not null)
            texto += $"\n\nDuração: {f.Duracao}";

        if (f.PrecisaAdmin)
            texto += "\n\nExige executar como administrador.";

        texto += $"\n\nComo desfazer: {f.ComoDesfazer}";

        MessageBox.Show(texto, f.Titulo, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
