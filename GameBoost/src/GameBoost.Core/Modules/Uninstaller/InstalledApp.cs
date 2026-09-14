namespace GameBoost.Core.Modules.Uninstaller;

public enum AppOrigem
{
    Win32,
    Store,
    Portatil
}

/// <summary>
/// Um aplicativo instalado, venha do registro ou da Store.
/// </summary>
public sealed record InstalledApp
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public AppOrigem Origem { get; init; } = AppOrigem.Win32;

    public string? Versao { get; init; }
    public string? Publisher { get; init; }
    public string? PastaDeInstalacao { get; init; }

    /// <summary>Do registro. Pouco confiável: serve como piso quando não dá para medir a pasta.</summary>
    public long TamanhoEstimado { get; init; }

    /// <summary>Medido na pasta de instalação. Zero quando não foi possível medir.</summary>
    public long TamanhoMedido { get; set; }

    public long Tamanho => TamanhoMedido > 0 ? TamanhoMedido : TamanhoEstimado;

    public DateTimeOffset? Instalado { get; init; }
    public DateTimeOffset? UltimoUso { get; set; }

    /// <summary>Comando de desinstalação silenciosa, quando o app oferece um.</summary>
    public string? ComandoSilencioso { get; init; }

    /// <summary>Comando normal: abre o desinstalador do próprio app.</summary>
    public string? Comando { get; init; }

    /// <summary>Pacote MSI: dá para desinstalar com msiexec /x {GUID} /qn.</summary>
    public string? CodigoMsi { get; init; }

    /// <summary>Nome completo do pacote da Store.</summary>
    public string? PacoteDaStore { get; init; }

    public bool Protegido { get; set; }
    public string? MotivoDaProtecao { get; set; }

    /// <summary>Bloatware conhecido. Sugerido na lista, nunca pré-marcado (regra 3).</summary>
    public bool Sugerido { get; set; }

    public bool TemDesinstalacaoSilenciosa
        => !string.IsNullOrWhiteSpace(ComandoSilencioso)
        || !string.IsNullOrWhiteSpace(CodigoMsi)
        || Origem == AppOrigem.Store;

    public string QuandoUsou => UltimoUso is null
        ? "sem registro de uso"
        : (DateTimeOffset.Now - UltimoUso.Value).TotalDays switch
        {
            < 7 => "usado esta semana",
            < 31 => "usado este mês",
            < 180 => $"usado há {(int)((DateTimeOffset.Now - UltimoUso.Value).TotalDays / 30)} meses",
            < 365 => "usado há mais de 6 meses",
            _ => "usado há mais de um ano"
        };

    /// <summary>Grande e parado: é onde o espaço realmente está.</summary>
    public bool NuncaUsado
        => UltimoUso is null
        || (DateTimeOffset.Now - UltimoUso.Value).TotalDays > 365;
}
