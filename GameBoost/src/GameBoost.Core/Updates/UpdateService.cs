using GameBoost.Core.Logging;
using GameBoost.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace GameBoost.Core.Updates;

/// <summary>Estado da checagem de atualização, para a tela mostrar.</summary>
public enum EstadoDaAtualizacao
{
    NaoVerificado,
    Verificando,
    EmDia,
    Disponivel,
    Baixando,
    ProntaParaInstalar,
    Indisponivel
}

/// <summary>
/// Atualização do próprio GameBoost, em cima do Velopack (seção 10).
///
/// Três coisas que ele **não** faz, e cada uma é uma decisão:
///
/// - **Não atualiza sozinho sem avisar.** A checagem na abertura é silenciosa, o
///   download é em segundo plano, mas a troca de versão só acontece no próximo
///   início e o usuário vê que ela está pendente.
/// - **Não sai para a rede sem permissão.** A mesma chave que libera o teste de
///   velocidade vale aqui. Desligada, o botão "Verificar" explica o que falta em
///   vez de falhar calado.
/// - **Não funciona no modo portátil, e diz isso.** O Velopack atualiza uma
///   instalação que ele mesmo montou; um exe solto numa pasta não tem o que
///   atualizar. Fingir que verificou seria pior que a mensagem honesta.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Repositório das releases. É público e não exige token.</summary>
    private const string Repositorio = "https://github.com/jvaz-DEVENG/showcase";

    private readonly ISettingsStore _configuracoes;
    private readonly AppPaths _paths;
    private readonly IGameBoostLogger _log;

    private UpdateManager? _gerente;
    private UpdateInfo? _pendente;

    public UpdateService(ISettingsStore configuracoes, AppPaths paths, IGameBoostLogger log)
    {
        _configuracoes = configuracoes;
        _paths = paths;
        _log = log;
    }

    public EstadoDaAtualizacao Estado { get; private set; } = EstadoDaAtualizacao.NaoVerificado;

    public string? VersaoDisponivel { get; private set; }

    public string? Motivo { get; private set; }

    /// <summary>Versão em execução agora.</summary>
    public string VersaoAtual =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "desconhecida";

    /// <summary>
    /// Só há o que atualizar quando o Velopack instalou o app. No modo portátil
    /// e durante o desenvolvimento não há pacote nenhum.
    /// </summary>
    public bool Suportado
    {
        get
        {
            if (_paths.Portatil)
                return false;

            try
            {
                return Gerente().IsInstalled;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                return false;
            }
        }
    }

    private UpdateManager Gerente()
        => _gerente ??= new UpdateManager(new GithubSource(Repositorio, null, prerelease: false));

    // ==================================================================

    /// <summary>
    /// Checagem silenciosa da abertura. Nunca mostra erro: se não deu, não deu,
    /// e o usuário descobre quando clicar em Verificar.
    /// </summary>
    public async Task VerificarNaAberturaAsync(CancellationToken ct)
    {
        var settings = _configuracoes.Load();

        if (!settings.VerificarAtualizacoes || !settings.PermitirAcessoARede || !Suportado)
            return;

        try
        {
            await VerificarAsync(ct);

            if (Estado == EstadoDaAtualizacao.Disponivel)
                await BaixarAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            _log.Info("update", "Abertura", null, $"checagem silenciosa falhou: {ex.Message}");
        }
    }

    public async Task<EstadoDaAtualizacao> VerificarAsync(CancellationToken ct)
    {
        if (_paths.Portatil)
        {
            Estado = EstadoDaAtualizacao.Indisponivel;
            Motivo = "No modo portátil não há instalação para atualizar. "
                   + "Baixe a versão nova pela página de releases quando quiser trocar.";
            return Estado;
        }

        if (!_configuracoes.Load().PermitirAcessoARede)
        {
            Estado = EstadoDaAtualizacao.Indisponivel;
            Motivo = "Verificar atualizações precisa de acesso à internet, e essa permissão "
                   + "está desligada. Ligue em Configurações, se quiser.";
            return Estado;
        }

        if (!Suportado)
        {
            Estado = EstadoDaAtualizacao.Indisponivel;
            Motivo = "Esta cópia não foi instalada pelo instalador do GameBoost, então não há "
                   + "o que atualizar por aqui.";
            return Estado;
        }

        Estado = EstadoDaAtualizacao.Verificando;
        Motivo = null;

        try
        {
            _pendente = await Gerente().CheckForUpdatesAsync().WaitAsync(ct);

            if (_pendente is null)
            {
                Estado = EstadoDaAtualizacao.EmDia;
                Motivo = $"Você está na versão {VersaoAtual}, que é a mais recente.";
                _log.Info("update", "Verificar", null, "em dia");
                return Estado;
            }

            VersaoDisponivel = _pendente.TargetFullRelease.Version.ToString();
            Estado = EstadoDaAtualizacao.Disponivel;
            Motivo = $"Versão {VersaoDisponivel} disponível. A sua é a {VersaoAtual}.";

            _log.Info("update", "Verificar", null, $"{VersaoAtual} -> {VersaoDisponivel}");
            return Estado;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Estado = EstadoDaAtualizacao.Indisponivel;
            Motivo = $"Não foi possível verificar: {ex.Message}";
            _log.Warn("update", "Verificar", null, ex.Message);
            return Estado;
        }
    }

    public async Task<EstadoDaAtualizacao> BaixarAsync(CancellationToken ct)
    {
        if (_pendente is null)
            return Estado;

        Estado = EstadoDaAtualizacao.Baixando;

        try
        {
            await Gerente().DownloadUpdatesAsync(_pendente).WaitAsync(ct);

            Estado = EstadoDaAtualizacao.ProntaParaInstalar;
            Motivo = $"Versão {VersaoDisponivel} baixada. Ela entra no próximo início do GameBoost.";

            _log.Info("update", "Baixar", VersaoDisponivel, "pronta para instalar");
            return Estado;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Estado = EstadoDaAtualizacao.Disponivel;
            Motivo = $"O download falhou: {ex.Message}";
            _log.Warn("update", "Baixar", VersaoDisponivel, ex.Message);
            return Estado;
        }
    }

    /// <summary>
    /// Aplica a atualização e reinicia. Só deve ser chamado por um botão que o
    /// usuário clicou: trocar a versão embaixo de alguém que está no meio de uma
    /// operação é exatamente o que dá errado.
    /// </summary>
    public void InstalarEReiniciar()
    {
        if (_pendente is null || Estado != EstadoDaAtualizacao.ProntaParaInstalar)
            return;

        _log.Info("update", "Instalar", VersaoDisponivel, "aplicando e reiniciando");
        Gerente().ApplyUpdatesAndRestart(_pendente);
    }
}
