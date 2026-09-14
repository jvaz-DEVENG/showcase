namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Valor que pode nao estar disponivel nesta maquina. Existe para a UI dizer
/// "indisponivel" em vez de mostrar zero, que seria mentira (regra 4).
/// </summary>
public readonly record struct Medida(double Valor, bool Disponivel)
{
    public static Medida Indisponivel { get; } = new(0, false);

    public static Medida De(double valor) => new(valor, true);

    public double OuZero => Disponivel ? Valor : 0;

    public override string ToString() => Disponivel ? Valor.ToString("0.0") : "indisponivel";
}

/// <summary>Consumo de um processo num instante.</summary>
public sealed record ProcessUsage(
    int Pid,
    string Nome,
    double CpuPercent,
    long WorkingSetBytes,
    long IoBytesPorSegundo,
    int Instancias)
{
    public string NomeExibido => Instancias > 1 ? $"{Nome} ({Instancias})" : Nome;
}

/// <summary>Um instante completo da maquina. Imutavel: vai direto para o buffer.</summary>
public sealed record MetricsSnapshot
{
    public required DateTimeOffset Momento { get; init; }

    // CPU
    public double CpuPercent { get; init; }
    public Medida CpuFrequenciaPercent { get; init; } = Medida.Indisponivel;
    public int NucleosLogicos { get; init; }

    // Memoria
    public long RamTotalBytes { get; init; }
    public long RamDisponivelBytes { get; init; }
    public long StandbyBytes { get; init; }
    public long CommitBytes { get; init; }

    // GPU
    public Medida GpuPercent { get; init; } = Medida.Indisponivel;
    public Medida GpuMemoriaBytes { get; init; } = Medida.Indisponivel;

    // Disco
    public Medida DiscoOcupadoPercent { get; init; } = Medida.Indisponivel;
    public Medida DiscoBytesPorSegundo { get; init; } = Medida.Indisponivel;
    public double DiscoSistemaUsadoPercent { get; init; }

    // Rede
    public Medida RedeBytesPorSegundo { get; init; } = Medida.Indisponivel;

    // Temperaturas
    public Medida TemperaturaCpu { get; init; } = Medida.Indisponivel;
    public Medida TemperaturaGpu { get; init; } = Medida.Indisponivel;

    public IReadOnlyList<ProcessUsage> Processos { get; init; } = Array.Empty<ProcessUsage>();

    public double RamUsadaPercent => RamTotalBytes == 0
        ? 0
        : (RamTotalBytes - RamDisponivelBytes) * 100.0 / RamTotalBytes;

    public double RamDisponivelPercent => RamTotalBytes == 0
        ? 0
        : RamDisponivelBytes * 100.0 / RamTotalBytes;

    public double StandbyPercent => RamTotalBytes == 0
        ? 0
        : StandbyBytes * 100.0 / RamTotalBytes;
}
