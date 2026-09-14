using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Uso e memoria da GPU pelos contadores "GPU Engine" e "GPU Adapter Memory"
/// (Windows 10 1709+).
///
/// Uma consulta PDH com curinga cobre todas as instancias de uma vez. A
/// primeira versao criava um PerformanceCounter por instancia e o ciclo de
/// coleta passava de 1 segundo com jogo e navegadores abertos, estourando o
/// orcamento da secao 5.5.
///
/// Se os contadores nao existirem, a GPU vira "indisponivel" e nada mais
/// quebra: a UI diz isso em vez de mostrar zero (regra 4).
/// </summary>
internal sealed class GpuCounterReader : IDisposable
{
    private const string CaminhoDeUso = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private const string CaminhoDeMemoria = @"\GPU Adapter Memory(*)\Dedicated Usage";

    private readonly IGameBoostLogger _log;
    private PdhQuery? _uso;
    private PdhQuery? _memoria;
    private bool _iniciado;
    private bool _descartado;

    public GpuCounterReader(IGameBoostLogger log)
    {
        _log = log;
    }

    private void GarantirIniciado()
    {
        if (_iniciado || _descartado)
            return;

        _iniciado = true;

        _uso = PdhQuery.Abrir(CaminhoDeUso);
        _memoria = PdhQuery.Abrir(CaminhoDeMemoria);

        if (_uso is null)
            _log.Warn("Bottleneck", "GPU", null,
                "contadores de GPU indisponiveis nesta maquina: o uso de GPU fica sem medida");
    }

    public Medida LerUso()
    {
        GarantirIniciado();

        if (_uso is null)
            return Medida.Indisponivel;

        var valores = _uso.Coletar();
        if (valores.Count == 0)
            return Medida.De(0);

        // Cada engine reporta a propria ocupacao; a placa esta tao ocupada
        // quanto a engine mais carregada, nao quanto a soma delas.
        return Medida.De(Math.Clamp(valores.Max(), 0, 100));
    }

    public Medida LerMemoria()
    {
        GarantirIniciado();

        if (_memoria is null)
            return Medida.Indisponivel;

        var valores = _memoria.Coletar();
        return valores.Count == 0 ? Medida.Indisponivel : Medida.De(valores.Sum());
    }

    public void Dispose()
    {
        _descartado = true;
        _uso?.Dispose();
        _memoria?.Dispose();
        _uso = null;
        _memoria = null;
    }
}
